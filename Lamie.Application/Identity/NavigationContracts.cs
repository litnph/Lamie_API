namespace Lamie.Application.Identity;

public sealed record NavigationManagementDto(
    Guid Id,
    string Key,
    Guid? ParentId,
    string? ModuleKey,
    string? PageKey,
    string Label,
    string? Description,
    string? Path,
    string? IconKey,
    string? PermissionCode,
    bool? PermissionIsActive,
    int SortOrder,
    bool IsVisible,
    bool IsEnabled,
    bool IsSystem,
    bool OpenInNewTab,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    IReadOnlyList<string> Warnings);

public sealed record CurrentNavigationItemDto(
    Guid Id,
    string Key,
    string Label,
    string? Description,
    string? Path,
    string? IconKey,
    string? PermissionCode,
    int SortOrder,
    bool OpenInNewTab,
    IReadOnlyList<CurrentNavigationItemDto> Children);

public sealed record CurrentNavigationRouteDto(
    Guid Id,
    string Key,
    string ModuleKey,
    string PageKey,
    string Path,
    string? PermissionCode,
    int SortOrder);

public sealed record SaveNavigationRequest(
    string Key,
    Guid? ParentId,
    string? ModuleKey,
    string? PageKey,
    string Label,
    string? Description,
    string? Path,
    string? IconKey,
    string? PermissionCode,
    int SortOrder,
    bool IsVisible,
    bool IsEnabled,
    bool OpenInNewTab);

public sealed record NavigationReorderItemRequest(Guid Id, Guid? ParentId, int SortOrder);

public sealed record NavigationReorderRequest(IReadOnlyList<NavigationReorderItemRequest> Items);

public interface INavigationService
{
    Task<IReadOnlyList<NavigationManagementDto>> GetNavigationAsync(
        CancellationToken cancellationToken);

    Task<NavigationManagementDto> GetNavigationAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrentNavigationItemDto>> GetCurrentUserNavigationAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrentNavigationRouteDto>> GetCurrentUserRoutesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<NavigationManagementDto> CreateNavigationAsync(
        SaveNavigationRequest request,
        CancellationToken cancellationToken);

    Task UpdateNavigationAsync(
        Guid id,
        SaveNavigationRequest request,
        CancellationToken cancellationToken);

    Task DeleteNavigationAsync(Guid id, CancellationToken cancellationToken);

    Task ReorderNavigationAsync(
        NavigationReorderRequest request,
        CancellationToken cancellationToken);

    Task SetNavigationEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken);
}
