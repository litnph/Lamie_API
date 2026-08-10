using Microsoft.AspNetCore.Authorization;

namespace Lamie.API.Authorization;

public sealed record PermissionRequirement(string PermissionCode) : IAuthorizationRequirement;
