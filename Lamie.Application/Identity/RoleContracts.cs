namespace Lamie.Application.Identity;

public sealed record PermissionDto(
    Guid Id,
    string Code,
    string Name,
    string Group,
    string? Description,
    bool IsSystem,
    bool IsActive);

public sealed record RoleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    int UserCount,
    IReadOnlyCollection<string> PermissionCodes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SaveRoleRequest(
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyCollection<string> PermissionCodes);

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(bool activeOnly, CancellationToken cancellationToken);
    Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken);
    Task<RoleDto> GetRoleAsync(Guid id, CancellationToken cancellationToken);
    Task<RoleDto> CreateRoleAsync(SaveRoleRequest request, CancellationToken cancellationToken);
    Task UpdateRoleAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken);
    Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken);
}
