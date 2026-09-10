using System.Security.Claims;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class NavigationService : INavigationService
{
    private readonly AppDbContext _dbContext;
    private readonly IUserPermissionResolver _permissionResolver;
    private readonly IAccessControlCache _cache;
    private readonly IAccessAuditWriter _auditWriter;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public NavigationService(
        AppDbContext dbContext,
        IUserPermissionResolver permissionResolver,
        IAccessControlCache cache,
        IAccessAuditWriter auditWriter,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _permissionResolver = permissionResolver;
        _cache = cache;
        _auditWriter = auditWriter;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<NavigationManagementDto>> GetNavigationAsync(
        CancellationToken cancellationToken)
    {
        var navigation = await _dbContext.Navigation.AsNoTracking()
            .OrderBy(item => item.ParentId)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Label)
            .ThenBy(item => item.Key)
            .ToListAsync(cancellationToken);
        var permissionStates = await GetPermissionStatesAsync(navigation, cancellationToken);
        return ToManagementDtos(navigation, permissionStates);
    }

    public async Task<NavigationManagementDto> GetNavigationAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var navigation = await _dbContext.Navigation.AsNoTracking()
            .ToListAsync(cancellationToken);
        var item = navigation.SingleOrDefault(candidate => candidate.Id == id)
            ?? throw new NotFoundException("Navigation", id);
        var permissionStates = await GetPermissionStatesAsync(navigation, cancellationToken);
        return ToManagementDtos(navigation, permissionStates)
            .Single(candidate => candidate.Id == item.Id);
    }

    public async Task<IReadOnlyList<CurrentNavigationItemDto>> GetCurrentUserNavigationAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetNavigation(userId, out var cached) && cached is not null)
            return cached;

        var authorization = await _permissionResolver.ResolveAsync(userId, cancellationToken);
        var navigation = await _dbContext.Navigation.AsNoTracking().ToListAsync(cancellationToken);
        var accessible = BuildAccessibleSet(navigation, authorization.PermissionCodes, requireVisible: true);
        var children = accessible.Values
            .Where(item => item.ParentId is not null && accessible.ContainsKey(item.ParentId.Value))
            .GroupBy(item => item.ParentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => Order(group).ToArray());

        CurrentNavigationItemDto Build(AdminNavigation item, HashSet<Guid> ancestors)
        {
            if (!ancestors.Add(item.Id))
                throw new InvalidOperationException("A navigation cycle escaped runtime validation.");
            var itemChildren = children.GetValueOrDefault(item.Id) ?? [];
            var result = new CurrentNavigationItemDto(
                item.Id,
                item.Key,
                item.Label,
                item.Description,
                item.Path,
                item.IconKey,
                item.PermissionCode,
                item.SortOrder,
                item.OpenInNewTab,
                itemChildren.Select(child => Build(child, new HashSet<Guid>(ancestors))).ToArray());
            return result;
        }

        var tree = Order(accessible.Values.Where(item => item.ParentId is null))
            .Select(item => Build(item, []))
            .ToArray();
        var prunedTree = PruneEmptyContainers(tree);
        _cache.SetNavigation(userId, prunedTree);
        return prunedTree;
    }

    public async Task<IReadOnlyList<CurrentNavigationRouteDto>> GetCurrentUserRoutesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetRoutes(userId, out var cached) && cached is not null)
            return cached;

        var authorization = await _permissionResolver.ResolveAsync(userId, cancellationToken);
        var navigation = await _dbContext.Navigation.AsNoTracking().ToListAsync(cancellationToken);
        var accessible = BuildAccessibleSet(navigation, authorization.PermissionCodes, requireVisible: false);
        var visible = BuildAccessibleSet(navigation, authorization.PermissionCodes, requireVisible: true);
        var byId = navigation.ToDictionary(item => item.Id);

        string? ActiveMenuKey(AdminNavigation route)
        {
            var current = route;
            var visited = new HashSet<Guid>();
            while (visited.Add(current.Id))
            {
                if (visible.ContainsKey(current.Id) && current.Path is not null)
                    return current.Key;
                if (!current.ParentId.HasValue || !byId.TryGetValue(current.ParentId.Value, out var parent))
                    return null;
                current = parent;
            }
            return null;
        }
        var routes = accessible.Values
            .Where(item => item.Path is not null && item.ModuleKey is not null && item.PageKey is not null)
            .OrderBy(item => item.Path)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Key)
            .Select(item => new CurrentNavigationRouteDto(
                item.Id,
                item.Key,
                item.ModuleKey!,
                item.PageKey!,
                item.Path!,
                item.PermissionCode,
                item.SortOrder,
                ActiveMenuKey(item)))
            .ToArray();
        _cache.SetRoutes(userId, routes);
        return routes;
    }

    public async Task<NavigationManagementDto> CreateNavigationAsync(
        SaveNavigationRequest request,
        CancellationToken cancellationToken)
    {
        var key = NormalizeKey(request.Key);
        if (await _dbContext.Navigation.AnyAsync(item => item.Key == key, cancellationToken))
            throw new ConflictException("Navigation key is already in use.");
        await ValidateLogicalReferencesAsync(null, request.ParentId, request.PermissionCode, cancellationToken);
        if (await _dbContext.Navigation.AnyAsync(
                item => item.ParentId == request.ParentId && item.SortOrder == request.SortOrder,
                cancellationToken))
        {
            throw Validation(nameof(request.SortOrder), "Sibling navigation sort orders must be unique.");
        }

        var now = UtcNow();
        var actorUserId = GetActorUserId();
        AdminNavigation item;
        try
        {
            item = new AdminNavigation(
                key,
                request.ParentId,
                request.ModuleKey,
                request.PageKey,
                request.Label,
                request.Description,
                request.Path,
                request.IconKey,
                request.PermissionCode,
                request.SortOrder,
                request.IsVisible,
                request.IsEnabled,
                false,
                request.OpenInNewTab,
                now,
                actorUserId);
        }
        catch (DomainException exception)
        {
            throw Validation(nameof(request), exception.Message);
        }

        _dbContext.Navigation.Add(item);
        _auditWriter.Record("create", "Navigation", item.Id.ToString(), null, Snapshot(item));
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("Navigation key is already in use.");
        }
        _cache.InvalidateNavigation();
        return await GetNavigationAsync(item.Id, cancellationToken);
    }

    public async Task UpdateNavigationAsync(
        Guid id,
        SaveNavigationRequest request,
        CancellationToken cancellationToken)
    {
        var item = await _dbContext.Navigation.SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken) ?? throw new NotFoundException("Navigation", id);
        var requestedKey = NormalizeKey(request.Key);
        if (!string.Equals(item.Key, requestedKey, StringComparison.Ordinal))
            throw Validation(nameof(request.Key), "Navigation key cannot be changed.");
        await ValidateLogicalReferencesAsync(id, request.ParentId, request.PermissionCode, cancellationToken);

        var before = Snapshot(item);
        try
        {
            item.Update(
                request.ParentId,
                request.ModuleKey,
                request.PageKey,
                request.Label,
                request.Description,
                request.Path,
                request.IconKey,
                request.PermissionCode,
                request.SortOrder,
                request.IsVisible,
                request.IsEnabled,
                request.OpenInNewTab,
                UtcNow(),
                GetActorUserId());
        }
        catch (DomainException exception)
        {
            throw Validation(nameof(request), exception.Message);
        }
        await ValidateTrackedTreeAsync(cancellationToken);
        _auditWriter.Record("update", "Navigation", item.Id.ToString(), before, Snapshot(item));
        await _dbContext.SaveChangesAsync(cancellationToken);
        _cache.InvalidateNavigation();
    }

    public async Task DeleteNavigationAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await _dbContext.Navigation.SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken) ?? throw new NotFoundException("Navigation", id);
        if (item.IsSystem)
            throw new ConflictException("System navigation cannot be deleted.");
        if (await _dbContext.Navigation.AnyAsync(candidate => candidate.ParentId == id, cancellationToken))
            throw new ConflictException("Navigation with children cannot be deleted.");

        _auditWriter.Record("delete", "Navigation", item.Id.ToString(), Snapshot(item), null);
        _dbContext.Navigation.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _cache.InvalidateNavigation();
    }

    public async Task ReorderNavigationAsync(
        NavigationReorderRequest request,
        CancellationToken cancellationToken)
    {
        var changes = request.Items ?? [];
        if (changes.Count == 0)
            throw Validation(nameof(request.Items), "At least one navigation item is required.");
        var duplicateIds = changes.GroupBy(item => item.Id).Where(group => group.Count() > 1).Select(group => group.Key);
        if (duplicateIds.Any())
            throw Validation(nameof(request.Items), "Navigation reorder contains duplicate item ids.");
        if (changes.Any(item => item.SortOrder < 0))
            throw Validation(nameof(request.Items), "Navigation sort order cannot be negative.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var all = await _dbContext.Navigation.ToListAsync(cancellationToken);
        var byId = all.ToDictionary(item => item.Id);
        var missing = changes.Where(change => !byId.ContainsKey(change.Id)).Select(change => change.Id).ToArray();
        if (missing.Length > 0)
            throw Validation(nameof(request.Items), $"Unknown navigation ids: {string.Join(", ", missing)}.");
        var actorUserId = GetActorUserId();
        var now = UtcNow();
        foreach (var change in changes)
        {
            var item = byId[change.Id];
            var before = Snapshot(item);
            item.Move(change.ParentId, change.SortOrder, now, actorUserId);
            _auditWriter.Record("reorder", "Navigation", item.Id.ToString(), before, Snapshot(item));
        }
        ValidateTree(all);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _cache.InvalidateNavigation();
    }

    public async Task SetNavigationEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var item = await _dbContext.Navigation.SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken) ?? throw new NotFoundException("Navigation", id);
        if (item.IsEnabled == isEnabled)
            return;

        var before = Snapshot(item);
        item.SetEnabled(isEnabled, UtcNow(), GetActorUserId());
        _auditWriter.Record(
            isEnabled ? "enable" : "disable",
            "Navigation",
            item.Id.ToString(),
            before,
            Snapshot(item));
        await _dbContext.SaveChangesAsync(cancellationToken);
        _cache.InvalidateNavigation();
    }

    private async Task ValidateLogicalReferencesAsync(
        Guid? currentId,
        Guid? parentId,
        string? permissionCode,
        CancellationToken cancellationToken)
    {
        if (currentId.HasValue && parentId == currentId)
            throw Validation(nameof(parentId), "Navigation cannot be its own parent.");
        if (parentId.HasValue
            && !await _dbContext.Navigation.AnyAsync(item => item.Id == parentId, cancellationToken))
        {
            throw Validation(nameof(parentId), "Navigation parent does not exist.");
        }
        if (!string.IsNullOrWhiteSpace(permissionCode))
        {
            string normalizedPermission;
            try
            {
                normalizedPermission = Permission.NormalizeCode(permissionCode);
            }
            catch (DomainException exception)
            {
                throw Validation(nameof(permissionCode), exception.Message);
            }
            if (!await _dbContext.Permissions.AnyAsync(
                    permission => permission.Code == normalizedPermission,
                    cancellationToken))
            {
                throw Validation(nameof(permissionCode), "Navigation permission does not exist.");
            }
        }
    }

    private async Task ValidateTrackedTreeAsync(CancellationToken cancellationToken)
    {
        var persisted = await _dbContext.Navigation.ToListAsync(cancellationToken);
        ValidateTree(persisted);
    }

    private static void ValidateTree(IReadOnlyCollection<AdminNavigation> navigation)
    {
        var byId = navigation.ToDictionary(item => item.Id);
        foreach (var item in navigation)
        {
            if (item.ParentId == item.Id)
                throw Validation(nameof(item.ParentId), "Navigation cannot be its own parent.");
            if (item.ParentId.HasValue && !byId.ContainsKey(item.ParentId.Value))
                throw Validation(nameof(item.ParentId), "Navigation parent does not exist.");

            var visited = new HashSet<Guid> { item.Id };
            var parentId = item.ParentId;
            while (parentId.HasValue)
            {
                if (!visited.Add(parentId.Value))
                    throw Validation(nameof(item.ParentId), "Navigation hierarchy contains a cycle.");
                parentId = byId[parentId.Value].ParentId;
            }
        }

        var duplicateOrders = navigation
            .GroupBy(item => new { item.ParentId, item.SortOrder })
            .Where(group => group.Count() > 1)
            .ToArray();
        if (duplicateOrders.Length > 0)
            throw Validation(nameof(AdminNavigation.SortOrder), "Sibling navigation sort orders must be unique.");
    }

    private static Dictionary<Guid, AdminNavigation> BuildAccessibleSet(
        IReadOnlyCollection<AdminNavigation> navigation,
        IReadOnlyCollection<string> permissionCodes,
        bool requireVisible)
    {
        var permissions = permissionCodes.ToHashSet(StringComparer.Ordinal);
        var byId = navigation.ToDictionary(item => item.Id);
        var accessible = new Dictionary<Guid, AdminNavigation>();

        bool IsAccessible(AdminNavigation item)
        {
            if (!item.IsEnabled || (requireVisible && !item.IsVisible))
                return false;
            if (item.PermissionCode is not null && !permissions.Contains(item.PermissionCode))
                return false;

            var visited = new HashSet<Guid> { item.Id };
            var parentId = item.ParentId;
            while (parentId.HasValue)
            {
                if (!visited.Add(parentId.Value)
                    || !byId.TryGetValue(parentId.Value, out var parent)
                    || !parent.IsEnabled
                    || (requireVisible && !parent.IsVisible)
                    || (parent.PermissionCode is not null && !permissions.Contains(parent.PermissionCode)))
                {
                    return false;
                }
                parentId = parent.ParentId;
            }
            return true;
        }

        foreach (var item in navigation.Where(IsAccessible))
            accessible.Add(item.Id, item);
        return accessible;
    }

    private async Task<Dictionary<string, bool>> GetPermissionStatesAsync(
        IReadOnlyCollection<AdminNavigation> navigation,
        CancellationToken cancellationToken)
    {
        var codes = navigation
            .Where(item => item.PermissionCode is not null)
            .Select(item => item.PermissionCode!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (codes.Length == 0)
            return new Dictionary<string, bool>(StringComparer.Ordinal);
        return await _dbContext.Permissions.AsNoTracking()
            .Where(permission => codes.Contains(permission.Code))
            .ToDictionaryAsync(permission => permission.Code, permission => permission.IsActive, cancellationToken);
    }

    private static IReadOnlyList<NavigationManagementDto> ToManagementDtos(
        IReadOnlyCollection<AdminNavigation> navigation,
        IReadOnlyDictionary<string, bool> permissionStates)
    {
        var ids = navigation.Select(item => item.Id).ToHashSet();
        return navigation.Select(item =>
        {
            var warnings = new List<string>();
            if (item.ParentId.HasValue && !ids.Contains(item.ParentId.Value))
                warnings.Add("parent_missing");
            bool? permissionIsActive = null;
            if (item.PermissionCode is not null)
            {
                if (!permissionStates.TryGetValue(item.PermissionCode, out var isActive))
                    warnings.Add("permission_missing");
                else
                {
                    permissionIsActive = isActive;
                    if (!isActive)
                        warnings.Add("permission_inactive");
                }
            }
            return new NavigationManagementDto(
                item.Id,
                item.Key,
                item.ParentId,
                item.ModuleKey,
                item.PageKey,
                item.Label,
                item.Description,
                item.Path,
                item.IconKey,
                item.PermissionCode,
                permissionIsActive,
                item.SortOrder,
                item.IsVisible,
                item.IsEnabled,
                item.IsSystem,
                item.OpenInNewTab,
                item.CreatedAt,
                item.UpdatedAt,
                item.CreatedBy,
                item.UpdatedBy,
                warnings);
        }).ToArray();
    }

    private static IOrderedEnumerable<AdminNavigation> Order(IEnumerable<AdminNavigation> items) =>
        items.OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .ThenBy(item => item.Key, StringComparer.Ordinal);

    private static IReadOnlyList<CurrentNavigationItemDto> PruneEmptyContainers(
        IReadOnlyCollection<CurrentNavigationItemDto> items) =>
        items.Select(item => item with { Children = PruneEmptyContainers(item.Children) })
            .Where(item => item.Path is not null || item.Children.Count > 0)
            .ToArray();

    private static object Snapshot(AdminNavigation item) => new
    {
        item.Key,
        item.ParentId,
        item.ModuleKey,
        item.PageKey,
        item.Label,
        item.Description,
        item.Path,
        item.IconKey,
        item.PermissionCode,
        item.SortOrder,
        item.IsVisible,
        item.IsEnabled,
        item.IsSystem,
        item.OpenInNewTab
    };

    private Guid? GetActorUserId()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var value = principal?.FindFirstValue("sub") ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string NormalizeKey(string value)
    {
        try
        {
            return AdminNavigation.NormalizeKey(value);
        }
        catch (DomainException exception)
        {
            throw Validation(nameof(value), exception.Message);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
