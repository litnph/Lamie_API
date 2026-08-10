using System.Security.Claims;
using Lamie.API.Authorization;
using Lamie.API.Controllers;
using Lamie.API.Services;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        Assert.Contains(PermissionNames.UsersManage, BuiltInRolePermissionDefaults.Get(BuiltInRole.Admin));
        Assert.DoesNotContain(PermissionNames.UsersView, BuiltInRolePermissionDefaults.Get(BuiltInRole.Manager));
        Assert.Contains(PermissionNames.ProductsManage, BuiltInRolePermissionDefaults.Get(BuiltInRole.Manager));
        Assert.DoesNotContain(PermissionNames.ProductsManage, BuiltInRolePermissionDefaults.Get(BuiltInRole.Staff));
        Assert.Contains(PermissionNames.OrdersManage, BuiltInRolePermissionDefaults.Get(BuiltInRole.Staff));
    }

    [Fact]
    public async Task PermissionPolicyDistinguishesChallengeForbiddenAndSuccessCases()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddLamieAuthorization();
        services.AddHttpContextAccessor();
        var userWithoutPermissionId = Guid.NewGuid();
        var authorizedUserId = Guid.NewGuid();
        services.AddSingleton<IUserPermissionResolver>(new StubPermissionResolver(
            authorizedUserId,
            PermissionNames.ProductsView));
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var authenticatedWithoutPermission = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userWithoutPermissionId.ToString())],
                "test"));
        var authorized = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, authorizedUserId.ToString())
                ],
                "test"));

        Assert.False((await authorization.AuthorizeAsync(anonymous, null, PermissionNames.ProductsView)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(authenticatedWithoutPermission, null, PermissionNames.ProductsView)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(authorized, null, PermissionNames.ProductsView)).Succeeded);

        var challengeContext = new DefaultHttpContext { RequestServices = provider };
        await challengeContext.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(StatusCodes.Status401Unauthorized, challengeContext.Response.StatusCode);

        challengeContext.Response.StatusCode = StatusCodes.Status200OK;
        challengeContext.User = authenticatedWithoutPermission;
        await challengeContext.ForbidAsync(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(StatusCodes.Status403Forbidden, challengeContext.Response.StatusCode);
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

    private sealed class StubPermissionResolver(Guid authorizedUserId, string permissionCode)
        : IUserPermissionResolver
    {
        public Task<UserAuthorizationSnapshot> ResolveAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            IReadOnlyCollection<string> permissions =
                userId == authorizedUserId ? [permissionCode] : [];
            return Task.FromResult(new UserAuthorizationSnapshot(
                Role.StaffId,
                "staff",
                "Staff",
                permissions));
        }
    }

}
