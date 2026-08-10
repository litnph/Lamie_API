using Lamie.Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Lamie.API.Authorization;

public sealed class PermissionAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider;

    public PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallbackProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallbackProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!IsPermissionPolicy(policyName))
            return _fallbackProvider.GetPolicyAsync(policyName);

        var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    private static bool IsPermissionPolicy(string policyName)
    {
        try
        {
            return policyName.Contains('.', StringComparison.Ordinal)
                && string.Equals(Permission.NormalizeCode(policyName), policyName, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
