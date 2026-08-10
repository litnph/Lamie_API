using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Lamie.API.Controllers;
using Lamie.API.Options;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lamie.Tests.Identity;

public sealed class RolePermissionTests
{
    private static readonly DateTime Baseline = new(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AccessControlEntitiesValidateCodesAssignmentsAndAdminProtection()
    {
        var role = new Role("  Accountant.Level-1  ", "Kế toán", null, true, Baseline);
        var assignment = new UserRole(Guid.NewGuid(), role.Id, Baseline);
        var grant = new RolePermission(role.Id, PermissionNames.IdFor(PermissionNames.ReportsView), Baseline);

        Assert.Equal("accountant.level-1", role.Code);
        Assert.Equal(role.Id, assignment.RoleId);
        Assert.Equal(role.Id, grant.RoleId);
        Assert.Throws<DomainException>(() => new Role("invalid code!", "Invalid", null, true, Baseline));
        Assert.Equal(Role.AdminId, Role.IdFor(BuiltInRole.Admin));
        Assert.Equal(BuiltInRole.Staff, Role.LegacyValueFor(role.Id));
    }

    [Fact]
    public void RolesControllerExposesProtectedManagementRoutes()
    {
        var authorize = Assert.Single(
            typeof(RolesController).GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.RolesView, authorize?.Policy);

        AssertRoute(nameof(RolesController.List), typeof(HttpGetAttribute), null);
        AssertRoute(nameof(RolesController.Permissions), typeof(HttpGetAttribute), "permissions");
        AssertRoute(nameof(RolesController.Get), typeof(HttpGetAttribute), "{id:guid}");
        AssertRoute(nameof(RolesController.Create), typeof(HttpPostAttribute), null, PermissionNames.RolesManage);
        AssertRoute(nameof(RolesController.Update), typeof(HttpPutAttribute), "{id:guid}", PermissionNames.RolesManage);
        AssertRoute(nameof(RolesController.Delete), typeof(HttpDeleteAttribute), "{id:guid}", PermissionNames.RolesManage);
    }

    [Fact]
    public void EfModelContainsTheFourForeignKeyFreeAccessControlModels()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieRoleModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        AssertAccessControlTable<Role>(model, "auth_roles");
        AssertAccessControlTable<Permission>(model, "auth_permissions");
        AssertAccessControlTable<UserRole>(model, "auth_user_roles");
        AssertAccessControlTable<RolePermission>(model, "auth_role_permissions");
        Assert.Equal(PermissionNames.All.Count, model.FindEntityType(typeof(Permission))!.GetSeedData().Count());
    }

    [Fact]
    public async Task PersistedRolePermissionsDriveLoginClaimsAndManagementSafety()
    {
        var databaseName = $"LamieRoleTests_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.MigrateAsync();
            Assert.Equal(3, await dbContext.Roles.CountAsync(role => role.IsSystem));
            Assert.Equal(PermissionNames.All.Count, await dbContext.Permissions.CountAsync());

            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var accessControlCache = new AccessControlCache(memoryCache);
            var httpContextAccessor = new HttpContextAccessor();
            var timeProvider = new FixedTimeProvider(Baseline);
            var auditWriter = new AccessAuditWriter(dbContext, httpContextAccessor, timeProvider);
            var permissionResolver = new UserPermissionResolver(dbContext, accessControlCache);
            var roleService = new RoleService(
                dbContext,
                timeProvider,
                accessControlCache,
                auditWriter);
            var permissionCatalog = await roleService.GetPermissionsAsync(CancellationToken.None);
            Assert.Equal(PermissionNames.All.Count, permissionCatalog.Count);
            Assert.All(permissionCatalog, permission =>
            {
                Assert.True(permission.IsSystem);
                Assert.True(permission.IsActive);
            });
            var custom = await roleService.CreateRoleAsync(
                new SaveRoleRequest(
                    "accountant",
                    "Kế toán",
                    "Theo dõi chi phí và báo cáo.",
                    true,
                    [PermissionNames.ExpensesView, PermissionNames.ReportsView]),
                CancellationToken.None);

            var passwordHasher = new PasswordHasher<User>();
            var user = new User(
                "accountant@lamie.test",
                "accountant",
                "pending-password-hash",
                "Lamie Accountant",
                null,
                BuiltInRole.Staff,
                true,
                Baseline);
            user.SetPasswordHash(passwordHasher.HashPassword(user, "StrongPass1"));
            dbContext.Users.Add(user);
            dbContext.UserRoles.Add(new UserRole(user.Id, custom.Id, Baseline));
            await dbContext.SaveChangesAsync();

            var jwt = new JwtTokenService(
                Options.Create(new JwtOptions
                {
                    Issuer = "role-tests",
                    Audience = "role-tests-client",
                    SigningKey = "role-tests-signing-key-with-at-least-32-bytes",
                    AccessTokenMinutes = 15,
                    RefreshTokenDays = 30
                }),
                new FixedTimeProvider(Baseline));
            var identity = new IdentityService(
                dbContext,
                passwordHasher,
                jwt,
                httpContextAccessor,
                timeProvider,
                permissionResolver,
                accessControlCache,
                auditWriter);

            var login = await identity.LoginAsync(
                new LoginRequest("accountant", "StrongPass1"),
                "127.0.0.1",
                CancellationToken.None);
            var accessToken = new JwtSecurityTokenHandler().ReadJwtToken(login.Tokens.AccessToken);
            var permissionClaims = accessToken.Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value).ToArray();

            Assert.Equal(custom.Id, login.User.RoleId);
            Assert.Equal("Kế toán", login.User.RoleName);
            Assert.Equal("accountant", accessToken.Payload["role"]?.ToString());
            Assert.Contains(PermissionNames.ExpensesView, permissionClaims);
            Assert.Contains(PermissionNames.ReportsView, permissionClaims);
            Assert.DoesNotContain(PermissionNames.ExpensesManage, permissionClaims);

            await roleService.UpdateRoleAsync(
                custom.Id,
                new SaveRoleRequest(
                    custom.Code,
                    custom.Name,
                    custom.Description,
                    true,
                    [PermissionNames.ExpensesView]),
                CancellationToken.None);
            var refreshedAuthorization = await permissionResolver.ResolveAsync(
                user.Id,
                CancellationToken.None);
            Assert.Contains(PermissionNames.ExpensesView, refreshedAuthorization.PermissionCodes);
            Assert.DoesNotContain(PermissionNames.ReportsView, refreshedAuthorization.PermissionCodes);
            Assert.All(
                await dbContext.RefreshTokens.Where(token => token.UserId == user.Id).ToListAsync(),
                token => Assert.NotNull(token.RevokedAt));
            await roleService.UpdateRoleAsync(
                custom.Id,
                new SaveRoleRequest(custom.Code, custom.Name, custom.Description, false, [PermissionNames.ExpensesView]),
                CancellationToken.None);
            await Assert.ThrowsAsync<UnauthorizedException>(() => identity.LoginAsync(
                new LoginRequest("accountant", "StrongPass1"),
                "127.0.0.1",
                CancellationToken.None));
            await Assert.ThrowsAsync<ConflictException>(() => roleService.DeleteRoleAsync(custom.Id, CancellationToken.None));

            var admin = await roleService.GetRoleAsync(Role.AdminId, CancellationToken.None);
            await Assert.ThrowsAsync<ConflictException>(() => roleService.UpdateRoleAsync(
                admin.Id,
                new SaveRoleRequest(admin.Code, admin.Name, admin.Description, true, [PermissionNames.RolesView]),
                CancellationToken.None));

            dbContext.UserRoles.Remove(await dbContext.UserRoles.SingleAsync(item => item.UserId == user.Id));
            await dbContext.SaveChangesAsync();
            await roleService.DeleteRoleAsync(custom.Id, CancellationToken.None);
            Assert.False(await dbContext.Roles.AnyAsync(role => role.Id == custom.Id));
            Assert.True(await dbContext.AccessAudits.AnyAsync(audit =>
                audit.EntityType == "Role" && audit.EntityId == custom.Id.ToString()));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task LoginRefreshUserRoleInvalidationAndFinalAdministratorRecoveryRemainIntegrated()
    {
        var databaseName = $"LamieIdentityIntegrationTests_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.MigrateAsync();
            Assert.Equal(3, await dbContext.Roles.CountAsync(role => role.IsSystem));
            Assert.Equal(PermissionNames.All.Count, await dbContext.Permissions.CountAsync());

            var passwordHasher = new PasswordHasher<User>();
            var administrator = new User(
                "recovery-admin@lamie.test",
                "recovery-admin",
                "pending-password-hash",
                "Recovery Administrator",
                null,
                BuiltInRole.Admin,
                true,
                Baseline);
            administrator.SetPasswordHash(passwordHasher.HashPassword(administrator, "StrongPass1"));
            var operatorUser = new User(
                "operator@lamie.test",
                "operator",
                "pending-password-hash",
                "Lamie Operator",
                null,
                BuiltInRole.Staff,
                true,
                Baseline);
            operatorUser.SetPasswordHash(passwordHasher.HashPassword(operatorUser, "StrongPass1"));
            dbContext.Users.AddRange(administrator, operatorUser);
            dbContext.UserRoles.AddRange(
                new UserRole(administrator.Id, Role.AdminId, Baseline),
                new UserRole(operatorUser.Id, Role.StaffId, Baseline));
            await dbContext.SaveChangesAsync();

            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var accessControlCache = new AccessControlCache(memoryCache);
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = AuthenticatedContext(administrator.Id)
            };
            var timeProvider = new FixedTimeProvider(Baseline);
            var auditWriter = new AccessAuditWriter(dbContext, httpContextAccessor, timeProvider);
            var permissionResolver = new UserPermissionResolver(dbContext, accessControlCache);
            var jwt = new JwtTokenService(
                Options.Create(new JwtOptions
                {
                    Issuer = "integration-tests",
                    Audience = "integration-tests-client",
                    SigningKey = "integration-tests-signing-key-with-at-least-32-bytes",
                    AccessTokenMinutes = 15,
                    RefreshTokenDays = 30
                }),
                timeProvider);
            var identity = new IdentityService(
                dbContext,
                passwordHasher,
                jwt,
                httpContextAccessor,
                timeProvider,
                permissionResolver,
                accessControlCache,
                auditWriter);

            var cachedStaff = await permissionResolver.ResolveAsync(operatorUser.Id, CancellationToken.None);
            Assert.DoesNotContain(PermissionNames.ProductsManage, cachedStaff.PermissionCodes);
            var login = await identity.LoginAsync(
                new LoginRequest(operatorUser.UserName, "StrongPass1"),
                "127.0.0.1",
                CancellationToken.None);
            var refreshed = await identity.RefreshAsync(
                new RefreshRequest(login.Tokens.RefreshToken),
                "127.0.0.1",
                CancellationToken.None);
            Assert.NotEqual(login.Tokens.RefreshToken, refreshed.Tokens.RefreshToken);
            Assert.Equal(Role.StaffId, refreshed.User.RoleId);
            Assert.Contains(PermissionNames.OrdersView, refreshed.User.Permissions!);
            var firstTokenHash = jwt.HashRefreshToken(login.Tokens.RefreshToken);
            var rotatedToken = await dbContext.RefreshTokens.SingleAsync(token => token.TokenHash == firstTokenHash);
            Assert.NotNull(rotatedToken.RevokedAt);
            Assert.NotNull(rotatedToken.ReplacedByTokenHash);

            await identity.UpdateUserAsync(
                operatorUser.Id,
                new UpdateUserRequest(
                    operatorUser.Id,
                    operatorUser.FullName,
                    operatorUser.Phone,
                    BuiltInRole.Manager,
                    true,
                    Role.ManagerId),
                CancellationToken.None);
            var resolvedManager = await permissionResolver.ResolveAsync(operatorUser.Id, CancellationToken.None);
            Assert.Equal(Role.ManagerId, resolvedManager.RoleId);
            Assert.Contains(PermissionNames.ProductsManage, resolvedManager.PermissionCodes);
            Assert.All(
                await dbContext.RefreshTokens.Where(token => token.UserId == operatorUser.Id).ToListAsync(),
                token => Assert.NotNull(token.RevokedAt));
            await Assert.ThrowsAsync<UnauthorizedException>(() => identity.RefreshAsync(
                new RefreshRequest(refreshed.Tokens.RefreshToken),
                "127.0.0.1",
                CancellationToken.None));
            Assert.True(await dbContext.AccessAudits.AnyAsync(audit =>
                audit.EntityType == "UserRole" && audit.EntityId == operatorUser.Id.ToString()));

            httpContextAccessor.HttpContext = AuthenticatedContext(operatorUser.Id);
            await Assert.ThrowsAsync<ConflictException>(() => identity.UpdateUserAsync(
                administrator.Id,
                new UpdateUserRequest(
                    administrator.Id,
                    administrator.FullName,
                    administrator.Phone,
                    BuiltInRole.Manager,
                    true,
                    Role.ManagerId),
                CancellationToken.None));
            await Assert.ThrowsAsync<ConflictException>(() => identity.DisableUserAsync(
                administrator.Id,
                "127.0.0.1",
                CancellationToken.None));

            var recoveryAdministrator = new User(
                "recovery-admin-2@lamie.test",
                "recovery-admin-2",
                "pending-password-hash",
                "Recovery Administrator 2",
                null,
                BuiltInRole.Admin,
                true,
                Baseline);
            recoveryAdministrator.SetPasswordHash(passwordHasher.HashPassword(recoveryAdministrator, "StrongPass1"));
            dbContext.Users.Add(recoveryAdministrator);
            dbContext.UserRoles.Add(new UserRole(recoveryAdministrator.Id, Role.AdminId, Baseline));
            await dbContext.SaveChangesAsync();

            await identity.DisableUserAsync(administrator.Id, "127.0.0.1", CancellationToken.None);
            Assert.False((await dbContext.Users.SingleAsync(user => user.Id == administrator.Id)).IsActive);
            Assert.True((await dbContext.Users.SingleAsync(user => user.Id == recoveryAdministrator.Id)).IsActive);
            Assert.Equal(
                Role.AdminId,
                (await dbContext.UserRoles.SingleAsync(item => item.UserId == recoveryAdministrator.Id)).RoleId);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    private static void AssertAccessControlTable<TEntity>(IModel model, string tableName)
    {
        var entity = model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entity);
        Assert.Equal(tableName, entity!.GetTableName());
        Assert.Empty(entity.GetForeignKeys());
    }

    private static DefaultHttpContext AuthenticatedContext(Guid userId) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", userId.ToString())],
            "test"))
    };

    private static void AssertRoute(
        string actionName,
        Type methodAttribute,
        string? template,
        string? policy = null)
    {
        var method = typeof(RolesController).GetMethod(actionName);
        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(methodAttribute, true)) as HttpMethodAttribute;
        Assert.Equal(template, route?.Template);
        if (policy is null)
            return;
        var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(policy, authorize?.Policy);
    }

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        private readonly DateTimeOffset _value = new(value);
        public override DateTimeOffset GetUtcNow() => _value;
    }
}
