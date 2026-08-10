using Lamie.API.Authorization;
using Lamie.API.Controllers;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lamie.Tests.Identity;

public sealed class PermissionFoundationTests
{
    private static readonly DateTime Baseline =
        new(2026, 8, 4, 4, 0, 0, DateTimeKind.Utc);

    private static readonly string[] OriginalPermissionCodes =
    [
        PermissionNames.ProductsView,
        PermissionNames.ProductsManage,
        PermissionNames.OrdersView,
        PermissionNames.OrdersManage,
        PermissionNames.OrdersCancel,
        PermissionNames.CustomersView,
        PermissionNames.CustomersManage,
        PermissionNames.ChannelsView,
        PermissionNames.ChannelsManage,
        PermissionNames.DashboardView,
        PermissionNames.SettingsView,
        PermissionNames.SettingsManage,
        PermissionNames.ExpensesView,
        PermissionNames.ExpensesManage,
        PermissionNames.ReportsView,
        PermissionNames.UsersView,
        PermissionNames.UsersManage,
        PermissionNames.RolesView,
        PermissionNames.RolesManage
    ];

    [Fact]
    public async Task DynamicPolicyProviderCreatesPermissionPoliciesWithoutAStaticRegistrationList()
    {
        var provider = new PermissionAuthorizationPolicyProvider(
            Options.Create(new AuthorizationOptions()));

        var policy = await provider.GetPolicyAsync("custom-area.approve");

        Assert.NotNull(policy);
        Assert.Contains(
            policy!.Requirements,
            requirement => requirement is PermissionRequirement permission
                && permission.PermissionCode == "custom-area.approve");
        Assert.Null(await provider.GetPolicyAsync("not a permission policy"));
    }

    [Fact]
    public void PermissionsControllerExposesProtectedCrudRoutes()
    {
        var authorize = Assert.Single(
            typeof(PermissionsController).GetCustomAttributes(typeof(AuthorizeAttribute), true))
            as AuthorizeAttribute;
        Assert.Equal(PermissionNames.RolesView, authorize?.Policy);

        AssertRoute(nameof(PermissionsController.List), typeof(HttpGetAttribute), null);
        AssertRoute(nameof(PermissionsController.Get), typeof(HttpGetAttribute), "{id:guid}");
        AssertRoute(
            nameof(PermissionsController.Create),
            typeof(HttpPostAttribute),
            null,
            PermissionNames.RolesManage);
        AssertRoute(
            nameof(PermissionsController.Update),
            typeof(HttpPutAttribute),
            "{id:guid}",
            PermissionNames.RolesManage);
        AssertRoute(
            nameof(PermissionsController.Delete),
            typeof(HttpDeleteAttribute),
            "{id:guid}",
            PermissionNames.RolesManage);
    }

    [Fact]
    public async Task PermissionCrudFilteringCacheInvalidationAndAuditAreDataDriven()
    {
        var databaseName = $"LamiePermissionTests_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.MigrateAsync();

            var migratedCodes = await dbContext.Permissions.AsNoTracking()
                .Select(permission => permission.Code)
                .ToArrayAsync();
            Assert.Equal(PermissionNames.All.Count, migratedCodes.Length);
            Assert.All(OriginalPermissionCodes, code => Assert.Contains(code, migratedCodes));
            Assert.Contains(PermissionNames.NavigationView, migratedCodes);
            Assert.Contains(PermissionNames.NavigationManage, migratedCodes);
            Assert.All(
                await dbContext.Permissions.AsNoTracking().ToArrayAsync(),
                permission =>
                {
                    Assert.True(permission.IsSystem);
                    Assert.True(permission.IsActive);
                    Assert.True(permission.SortOrder > 0);
                    Assert.NotEqual(default, permission.CreatedAt);
                    Assert.NotEqual(default, permission.UpdatedAt);
                });
            Assert.Equal(0, await CountForeignKeysAsync(dbContext));

            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cache = new AccessControlCache(memoryCache);
            var httpContextAccessor = new HttpContextAccessor();
            var timeProvider = new FixedTimeProvider(Baseline);
            var auditWriter = new AccessAuditWriter(dbContext, httpContextAccessor, timeProvider);
            var service = new PermissionManagementService(
                dbContext,
                cache,
                auditWriter,
                timeProvider);

            var created = await service.CreatePermissionAsync(
                new CreatePermissionRequest(
                    "  inventory.approve  ",
                    "Approve inventory",
                    "Approves inventory corrections.",
                    "Inventory",
                    true,
                    15),
                CancellationToken.None);
            Assert.Equal("inventory.approve", created.Code);
            Assert.False(created.IsSystem);
            Assert.True(created.IsActive);
            Assert.Equal(1, created.RoleCount);
            Assert.True(await dbContext.RolePermissions.AnyAsync(grant =>
                grant.RoleId == Role.AdminId && grant.PermissionId == created.Id));

            var filtered = await service.GetPermissionsAsync(
                "approve",
                "Inventory",
                false,
                true,
                1,
                10,
                CancellationToken.None);
            var filteredPermission = Assert.Single(filtered.Items);
            Assert.Equal(created.Id, filteredPermission.Id);
            Assert.Equal(1, filtered.TotalCount);
            Assert.False(filtered.HasNext);

            await Assert.ThrowsAsync<ConflictException>(() => service.CreatePermissionAsync(
                new CreatePermissionRequest(
                    "inventory.approve",
                    "Duplicate",
                    null,
                    "Inventory",
                    true,
                    20),
                CancellationToken.None));
            await Assert.ThrowsAsync<ValidationException>(() => service.UpdatePermissionAsync(
                created.Id,
                new UpdatePermissionRequest(
                    "inventory.reject",
                    created.Name,
                    created.Description,
                    created.Group,
                    true,
                    created.SortOrder),
                CancellationToken.None));
            await Assert.ThrowsAsync<ConflictException>(() => service.UpdatePermissionAsync(
                PermissionNames.IdFor(PermissionNames.ProductsView),
                new UpdatePermissionRequest(
                    PermissionNames.ProductsView,
                    "Changed system permission",
                    null,
                    "Products",
                    true,
                    10),
                CancellationToken.None));
            await Assert.ThrowsAsync<ConflictException>(() => service.DeactivatePermissionAsync(
                PermissionNames.IdFor(PermissionNames.ProductsView),
                CancellationToken.None));

            var role = new Role(
                "inventory-controller",
                "Inventory controller",
                null,
                true,
                Baseline);
            var user = new User(
                "inventory-controller@lamie.test",
                "inventory-controller",
                "not-used-by-this-test",
                "Inventory Controller",
                null,
                BuiltInRole.Staff,
                true,
                Baseline);
            dbContext.Roles.Add(role);
            dbContext.Users.Add(user);
            dbContext.UserRoles.Add(new UserRole(user.Id, role.Id, Baseline));
            dbContext.RolePermissions.Add(new RolePermission(role.Id, created.Id, Baseline));
            await dbContext.SaveChangesAsync();

            var resolver = new UserPermissionResolver(dbContext, cache);
            var beforeDeactivate = await resolver.ResolveAsync(user.Id, CancellationToken.None);
            Assert.Contains(created.Code, beforeDeactivate.PermissionCodes);

            await service.UpdatePermissionAsync(
                created.Id,
                new UpdatePermissionRequest(
                    created.Code,
                    "Approve stock corrections",
                    created.Description,
                    created.Group,
                    false,
                    25),
                CancellationToken.None);

            var afterDeactivate = await resolver.ResolveAsync(user.Id, CancellationToken.None);
            Assert.DoesNotContain(created.Code, afterDeactivate.PermissionCodes);
            var updated = await service.GetPermissionAsync(created.Id, CancellationToken.None);
            Assert.Equal("Approve stock corrections", updated.Name);
            Assert.False(updated.IsActive);
            Assert.Equal(25, updated.SortOrder);
            Assert.Equal(2, updated.RoleCount);

            var synchronizer = new PermissionCatalogSynchronizer(dbContext, cache);
            await synchronizer.SynchronizeAsync(CancellationToken.None);
            var permissionCount = await dbContext.Permissions.CountAsync();
            var grantCount = await dbContext.RolePermissions.CountAsync();
            await synchronizer.SynchronizeAsync(CancellationToken.None);
            Assert.Equal(permissionCount, await dbContext.Permissions.CountAsync());
            Assert.Equal(grantCount, await dbContext.RolePermissions.CountAsync());
            Assert.Equal(2, await dbContext.AccessAudits.CountAsync(audit =>
                audit.EntityType == "Permission" && audit.EntityId == created.Id.ToString()));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    private static async Task<int> CountForeignKeysAsync(AppDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static void AssertRoute(
        string actionName,
        Type methodAttribute,
        string? template,
        string? policy = null)
    {
        var method = typeof(PermissionsController).GetMethod(actionName);
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
