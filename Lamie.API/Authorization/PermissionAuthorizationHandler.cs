using System.Security.Claims;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;

namespace Lamie.API.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserPermissionResolver _permissionResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionAuthorizationHandler(
        IUserPermissionResolver permissionResolver,
        IHttpContextAccessor httpContextAccessor)
    {
        _permissionResolver = permissionResolver;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var subject = context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var userId))
            return;

        try
        {
            var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
            var authorization = await _permissionResolver.ResolveAsync(userId, cancellationToken);
            if (authorization.PermissionCodes.Contains(requirement.PermissionCode, StringComparer.Ordinal))
                context.Succeed(requirement);
        }
        catch (UnauthorizedException)
        {
        }
    }
}
