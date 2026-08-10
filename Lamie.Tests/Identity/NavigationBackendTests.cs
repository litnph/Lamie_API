using System.Security.Claims;
using Lamie.API.Controllers;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Lamie.Tests.Identity;

public sealed class NavigationBackendTests
{
    private static readonly DateTime Baseline =
        new(2026, 8, 4, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NavigationEntityRejectsUnsafeOrIncompleteDatabaseRoutes()
    {
        Assert.Throws<DomainException>(() => CreateEntity(path: "https://example.test/admin/products"));
        Assert.Throws<DomainException>(() => CreateEntity(path: "/admin/products?mode=edit"));
        Assert.Throws<DomainException>(() => CreateEntity(path: "/admin/products#edit"));
        Assert.Throws<DomainException>(() => CreateEntity(path: "/admin/../products"));
        Assert.Throws<DomainException>(() => CreateEntity(path: "/admin/products/:id", isVisible: true));
        Assert.Throws<DomainException>(() => CreateEntity(path: "/admin/products", pageKey: null));
        Assert.Throws<DomainException>(() => CreateEntity(path: "/admin/products", openInNewTab: true));

        var hiddenParameterizedRoute = CreateEntity(
            path: "/admin/products/:product_id",
            isVisible: false);
        Assert.Equal("/admin/products/:product_id", hiddenParameterizedRoute.Path);
    }

    [Fact]
    public void NavigationControllerSeparatesAuthenticatedRuntimeAndProtectedManagementRoutes()
    {
        var controllerAuthorize = Assert.Single(
            typeof(NavigationController).GetCustomAttributes(typeof(AuthorizeAttribute), true))
            as AuthorizeAttribute;
        Assert.Null(controllerAuthorize?.Policy);

        AssertRoute(nameof(NavigationController.Me), typeof(HttpGetAttribute), "me");
        AssertRoute(nameof(NavigationController.MyRoutes), typeof(HttpGetAttribute), "me/routes");
        AssertRoute(
            nameof(NavigationController.List),
            typeof(HttpGetAttribute),
            null,
            PermissionNames.NavigationView);
        AssertRoute(
            nameof(NavigationController.Get),
            typeof(HttpGetAttribute),
            "{id:guid}",
            PermissionNames.NavigationView);
        AssertRoute(
            nameof(NavigationController.Create),
            typeof(HttpPostAttribute),
            null,
            PermissionNames.NavigationManage);
        AssertRoute(
            nameof(NavigationController.Update),
            typeof(HttpPutAttribute),
            "{id:guid}",
            PermissionNames.NavigationManage);
        AssertRoute(
            nameof(NavigationController.Delete),
            typeof(HttpDeleteAttribute),
            "{id:guid}",
            PermissionNames.NavigationManage);
        AssertRoute(
            nameof(NavigationController.Reorder),
            typeof(HttpPostAttribute),
            "reorder",
            PermissionNames.NavigationManage);
        AssertRoute(
            nameof(NavigationController.Enable),
            typeof(HttpPostAttribute),
            "{id:guid}/enable",
            PermissionNames.NavigationManage);
        AssertRoute(
            nameof(NavigationController.Disable),
            typeof(HttpPostAttribute),
            "{id:guid}/disable",
            PermissionNames.NavigationManage);
    }

    [Fact]
    public async Task DefaultNavigationMigrationIsInsertOnlyIdempotentAndForeignKeyFree()
    {
        var databaseName = $"LamieNavigationSeedTests_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.GetService<IMigrator>()
                .MigrateAsync("20260804032612_AddNavigationBackend");
            var operatorOwnedId = Guid.NewGuid();
            dbContext.Navigation.Add(new AdminNavigation(
                "dashboard.home",
                null,
                "dashboard",
                "dashboard.home",
                "Operator dashboard label",
                "Must not be overwritten by the source seed.",
                "/admin/dashboard",
                "circle",
                PermissionNames.DashboardView,
                10,
                true,
                true,
                false,
                false,
                Baseline,
                null,
                operatorOwnedId));
            await dbContext.SaveChangesAsync();

            await dbContext.Database.MigrateAsync();
            await dbContext.Database.MigrateAsync();

            Assert.Equal(25, await dbContext.Navigation.CountAsync());
            Assert.Equal(25, await dbContext.Navigation.Select(item => item.Key).Distinct().CountAsync());
            Assert.Equal(10, await dbContext.Navigation.CountAsync(item => !item.IsVisible));
            Assert.Equal(0, await CountForeignKeysAsync(dbContext));
            var operatorOwned = await dbContext.Navigation.SingleAsync(item => item.Key == "dashboard.home");
            Assert.Equal(operatorOwnedId, operatorOwned.Id);
            Assert.Equal("Operator dashboard label", operatorOwned.Label);
            Assert.False(operatorOwned.IsSystem);
            var allIds = (await dbContext.Navigation.Select(item => item.Id).ToArrayAsync()).ToHashSet();
            Assert.All(
                await dbContext.Navigation.Where(item => item.ParentId != null).ToListAsync(),
                item => Assert.Contains(item.ParentId!.Value, allIds));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task NavigationCrudTreeRoutesReorderCacheAndAuditArePermissionAware()
    {
        var databaseName = $"LamieNavigationTests_{Guid.NewGuid():N}";
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
            Assert.Equal(0, await CountForeignKeysAsync(dbContext));
            Assert.Equal(25, await dbContext.Navigation.CountAsync());
            dbContext.Navigation.RemoveRange(dbContext.Navigation);
            await dbContext.SaveChangesAsync();

            var adminUser = CreateUser("navigation-admin", BuiltInRole.Admin);
            var staffUser = CreateUser("navigation-staff", BuiltInRole.Staff);
            var inactivePermission = new Permission(
                "archive.view",
                "View archive",
                null,
                "Archive",
                false,
                false,
                1,
                Baseline);
            dbContext.Users.AddRange(adminUser, staffUser);
            dbContext.UserRoles.AddRange(
                new UserRole(adminUser.Id, Role.AdminId, Baseline),
                new UserRole(staffUser.Id, Role.StaffId, Baseline));
            dbContext.Permissions.Add(inactivePermission);
            await dbContext.SaveChangesAsync();

            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", adminUser.Id.ToString())],
                        "test"))
                }
            };
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cache = new AccessControlCache(memoryCache);
            var resolver = new UserPermissionResolver(dbContext, cache);
            var auditWriter = new AccessAuditWriter(
                dbContext,
                httpContextAccessor,
                new FixedTimeProvider(Baseline));
            var service = new NavigationService(
                dbContext,
                resolver,
                cache,
                auditWriter,
                httpContextAccessor,
                new FixedTimeProvider(Baseline));

            var root = await service.CreateNavigationAsync(
                GroupRequest("operations", "Operations", 0),
                CancellationToken.None);
            var products = await service.CreateNavigationAsync(
                RouteRequest(
                    "products.list",
                    root.Id,
                    "Products",
                    "/admin/products",
                    PermissionNames.ProductsView,
                    10,
                    true),
                CancellationToken.None);
            var edit = await service.CreateNavigationAsync(
                RouteRequest(
                    "products.edit",
                    root.Id,
                    "Edit product",
                    "/admin/products/:id",
                    PermissionNames.ProductsManage,
                    20,
                    false),
                CancellationToken.None);
            var navigationAdmin = await service.CreateNavigationAsync(
                RouteRequest(
                    "navigation.list",
                    root.Id,
                    "Navigation",
                    "/admin/access/navigation",
                    PermissionNames.NavigationManage,
                    30,
                    true,
                    moduleKey: "navigation",
                    pageKey: "navigation.list"),
                CancellationToken.None);
            var disabled = await service.CreateNavigationAsync(
                RouteRequest(
                    "products.disabled",
                    root.Id,
                    "Disabled product page",
                    "/admin/products/disabled",
                    PermissionNames.ProductsView,
                    40,
                    true,
                    isEnabled: false),
                CancellationToken.None);
            var inactive = await service.CreateNavigationAsync(
                RouteRequest(
                    "archive.list",
                    root.Id,
                    "Archive",
                    "/admin/archive",
                    inactivePermission.Code,
                    50,
                    true,
                    moduleKey: "archive",
                    pageKey: "archive.list"),
                CancellationToken.None);
            var emptyGroup = await service.CreateNavigationAsync(
                GroupRequest("empty.group", "Empty group", 90),
                CancellationToken.None);

            var systemItem = new AdminNavigation(
                "system.help",
                null,
                "system",
                "system.help",
                "System help",
                null,
                "/admin/system-help",
                "help-circle",
                null,
                100,
                true,
                true,
                true,
                false,
                Baseline,
                adminUser.Id);
            dbContext.Navigation.Add(systemItem);
            await dbContext.SaveChangesAsync();

            var staffMenu = await service.GetCurrentUserNavigationAsync(
                staffUser.Id,
                CancellationToken.None);
            var operations = Assert.Single(staffMenu, item => item.Id == root.Id);
            Assert.Contains(operations.Children, item => item.Id == products.Id);
            Assert.DoesNotContain(operations.Children, item => item.Id == edit.Id);
            Assert.DoesNotContain(operations.Children, item => item.Id == navigationAdmin.Id);
            Assert.DoesNotContain(operations.Children, item => item.Id == disabled.Id);
            Assert.DoesNotContain(operations.Children, item => item.Id == inactive.Id);
            Assert.DoesNotContain(staffMenu, item => item.Id == emptyGroup.Id);

            var staffRoutes = await service.GetCurrentUserRoutesAsync(
                staffUser.Id,
                CancellationToken.None);
            Assert.Contains(staffRoutes, item => item.Id == products.Id);
            Assert.DoesNotContain(staffRoutes, item => item.Id == edit.Id);

            var adminRoutes = await service.GetCurrentUserRoutesAsync(
                adminUser.Id,
                CancellationToken.None);
            Assert.Contains(adminRoutes, item => item.Id == edit.Id);
            await service.SetNavigationEnabledAsync(edit.Id, false, CancellationToken.None);
            Assert.DoesNotContain(
                await service.GetCurrentUserRoutesAsync(adminUser.Id, CancellationToken.None),
                item => item.Id == edit.Id);
            await service.SetNavigationEnabledAsync(edit.Id, true, CancellationToken.None);
            Assert.Contains(
                await service.GetCurrentUserRoutesAsync(adminUser.Id, CancellationToken.None),
                item => item.Id == edit.Id);

            await service.ReorderNavigationAsync(
                new NavigationReorderRequest(
                [
                    new NavigationReorderItemRequest(products.Id, root.Id, 30),
                    new NavigationReorderItemRequest(navigationAdmin.Id, root.Id, 10)
                ]),
                CancellationToken.None);
            Assert.Equal(30, (await service.GetNavigationAsync(products.Id, CancellationToken.None)).SortOrder);
            Assert.Equal(10, (await service.GetNavigationAsync(navigationAdmin.Id, CancellationToken.None)).SortOrder);

            var inactiveManagement = await service.GetNavigationAsync(inactive.Id, CancellationToken.None);
            Assert.False(inactiveManagement.PermissionIsActive);
            Assert.Contains("permission_inactive", inactiveManagement.Warnings);
            Assert.Equal(adminUser.Id, root.CreatedBy);

            await Assert.ThrowsAsync<ValidationException>(() => service.UpdateNavigationAsync(
                products.Id,
                RouteRequest(
                    "products.renamed",
                    root.Id,
                    "Products",
                    "/admin/products",
                    PermissionNames.ProductsView,
                    30,
                    true),
                CancellationToken.None));
            await Assert.ThrowsAsync<ValidationException>(() => service.CreateNavigationAsync(
                RouteRequest(
                    "unsafe.visible-parameter",
                    root.Id,
                    "Unsafe",
                    "/admin/unsafe/:id",
                    PermissionNames.ProductsView,
                    60,
                    true),
                CancellationToken.None));
            await Assert.ThrowsAsync<ValidationException>(() => service.CreateNavigationAsync(
                RouteRequest(
                    "unknown.permission",
                    root.Id,
                    "Unknown permission",
                    "/admin/unknown-permission",
                    "missing.permission",
                    60,
                    true),
                CancellationToken.None));
            await Assert.ThrowsAsync<ValidationException>(() => service.CreateNavigationAsync(
                RouteRequest(
                    "duplicate.order",
                    root.Id,
                    "Duplicate order",
                    "/admin/duplicate-order",
                    PermissionNames.ProductsView,
                    30,
                    true),
                CancellationToken.None));
            await Assert.ThrowsAsync<ConflictException>(() =>
                service.DeleteNavigationAsync(root.Id, CancellationToken.None));
            await Assert.ThrowsAsync<ConflictException>(() =>
                service.DeleteNavigationAsync(systemItem.Id, CancellationToken.None));

            Assert.True(await dbContext.AccessAudits.CountAsync(audit =>
                audit.EntityType == "Navigation" && audit.ActorUserId == adminUser.Id) >= 9);

            await Assert.ThrowsAsync<ValidationException>(() => service.ReorderNavigationAsync(
                new NavigationReorderRequest(
                [
                    new NavigationReorderItemRequest(root.Id, products.Id, 0)
                ]),
                CancellationToken.None));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    private static AdminNavigation CreateEntity(
        string? path,
        string? pageKey = "products.list",
        bool isVisible = false,
        bool openInNewTab = false) =>
        new(
            "products.list",
            null,
            "products",
            pageKey,
            "Products",
            null,
            path,
            "package",
            PermissionNames.ProductsView,
            10,
            isVisible,
            true,
            false,
            openInNewTab,
            Baseline,
            null);

    private static SaveNavigationRequest GroupRequest(string key, string label, int sortOrder) =>
        new(
            key,
            null,
            null,
            null,
            label,
            null,
            null,
            "folder",
            null,
            sortOrder,
            true,
            true,
            false);

    private static SaveNavigationRequest RouteRequest(
        string key,
        Guid parentId,
        string label,
        string path,
        string? permissionCode,
        int sortOrder,
        bool isVisible,
        string moduleKey = "products",
        string pageKey = "products.list",
        bool isEnabled = true) =>
        new(
            key,
            parentId,
            moduleKey,
            pageKey,
            label,
            null,
            path,
            "circle",
            permissionCode,
            sortOrder,
            isVisible,
            isEnabled,
            false);

    private static User CreateUser(string userName, BuiltInRole role) =>
        new(
            $"{userName}@lamie.test",
            userName,
            "not-used-by-this-test",
            userName,
            null,
            role,
            true,
            Baseline);

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
        var method = typeof(NavigationController).GetMethod(actionName);
        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(methodAttribute, true)) as HttpMethodAttribute;
        Assert.Equal(template, route?.Template);
        var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ToArray();
        if (policy is null)
            Assert.Empty(authorize);
        else
            Assert.Equal(policy, Assert.Single(authorize).Policy);
    }

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        private readonly DateTimeOffset _value = new(value);
        public override DateTimeOffset GetUtcNow() => _value;
    }
}
