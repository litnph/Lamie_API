using System.Security.Claims;
using Lamie.API.Authorization;
using Lamie.API.Controllers;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lamie.Tests.Identity;

public sealed class IdentityControllerContractTests
{
    [Fact]
    public void AuthControllerExposesTheFiveFrontendRoutes()
    {
        AssertRoute<AuthController>("Login", "login", typeof(HttpPostAttribute), allowAnonymous: true);
        AssertRoute<AuthController>("Refresh", "refresh", typeof(HttpPostAttribute), allowAnonymous: true);
        AssertRoute<AuthController>("Me", "me", typeof(HttpGetAttribute));
        AssertRoute<AuthController>("Logout", "logout", typeof(HttpPostAttribute));
        AssertRoute<AuthController>("ChangePassword", "change-password", typeof(HttpPostAttribute));
    }

    [Fact]
    public void UsersControllerExposesTheSixFrontendRoutesAndRequiresUserPermissions()
    {
        var authorize = Assert.Single(typeof(UsersController).GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.UsersView, authorize?.Policy);

        AssertRoute<UsersController>("List", null, typeof(HttpGetAttribute));
        AssertRoute<UsersController>("Get", "{id:guid}", typeof(HttpGetAttribute));
        AssertRoute<UsersController>("Create", null, typeof(HttpPostAttribute));
        AssertRoute<UsersController>("Update", "{id:guid}", typeof(HttpPutAttribute));
        AssertRoute<UsersController>("Disable", "{id:guid}", typeof(HttpDeleteAttribute));
        AssertRoute<UsersController>("ResetPassword", "{id:guid}/reset-password", typeof(HttpPatchAttribute));
    }

    [Fact]
    public void RolePermissionMappingMatchesFrontendRoles()
    {
        Assert.Contains(PermissionNames.UsersManage, RolePermissions.Get(UserRole.Admin));
        Assert.DoesNotContain(PermissionNames.UsersView, RolePermissions.Get(UserRole.Manager));
        Assert.Contains(PermissionNames.ProductsManage, RolePermissions.Get(UserRole.Manager));
        Assert.DoesNotContain(PermissionNames.ProductsManage, RolePermissions.Get(UserRole.Staff));
        Assert.Contains(PermissionNames.OrdersManage, RolePermissions.Get(UserRole.Staff));
    }

    [Fact]
    public async Task PermissionPolicyDistinguishesChallengeForbiddenAndSuccessCases()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLamieAuthorization();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var authenticatedWithoutPermission = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "test"));
        var authorized = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user"),
                    new Claim("permission", PermissionNames.ProductsView)
                ],
                "test"));

        Assert.False((await authorization.AuthorizeAsync(anonymous, null, PermissionNames.ProductsView)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(authenticatedWithoutPermission, null, PermissionNames.ProductsView)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(authorized, null, PermissionNames.ProductsView)).Succeeded);
    }

    private static void AssertRoute<TController>(
        string methodName,
        string? template,
        Type attributeType,
        bool allowAnonymous = false)
    {
        var method = typeof(TController).GetMethod(methodName);
        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(attributeType, true)) as HttpMethodAttribute;
        Assert.Equal(template, route?.Template);
        if (allowAnonymous)
            Assert.NotEmpty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
