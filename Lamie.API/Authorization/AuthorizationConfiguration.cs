using Microsoft.AspNetCore.Authorization;

namespace Lamie.API.Authorization;

public static class AuthorizationConfiguration
{
    public static IServiceCollection AddLamieAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}
