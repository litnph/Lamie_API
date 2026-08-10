namespace Lamie.Application.Identity;

public sealed record PermissionManagementDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Group,
    bool IsSystem,
    bool IsActive,
    int SortOrder,
    int RoleCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record PagedPermissionsDto(
    IReadOnlyList<PermissionManagementDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record CreatePermissionRequest(
    string Code,
    string Name,
    string? Description,
    string Group,
    bool IsActive,
    int SortOrder);

public sealed record UpdatePermissionRequest(
    string Code,
    string Name,
    string? Description,
    string Group,
    bool IsActive,
    int SortOrder);

public interface IPermissionManagementService
{
    Task<PagedPermissionsDto> GetPermissionsAsync(
        string? search,
        string? group,
        bool? system,
        bool? active,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PermissionManagementDto> GetPermissionAsync(Guid id, CancellationToken cancellationToken);

    Task<PermissionManagementDto> CreatePermissionAsync(
        CreatePermissionRequest request,
        CancellationToken cancellationToken);

    Task UpdatePermissionAsync(
        Guid id,
        UpdatePermissionRequest request,
        CancellationToken cancellationToken);

    Task DeactivatePermissionAsync(Guid id, CancellationToken cancellationToken);
}
