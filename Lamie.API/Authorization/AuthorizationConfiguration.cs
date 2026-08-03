using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Lamie.API.Authorization;

public static class AuthorizationConfiguration
{
    public static IServiceCollection AddLamieAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in PermissionNames.All)
            {
                options.AddPolicy(permission, policy =>
                    policy.RequireAuthenticatedUser()
                        .RequireClaim("permission", permission));
            }
        });

        return services;
    }
}
