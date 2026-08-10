using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class PermissionManagementService : IPermissionManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IAccessControlCache _cache;
    private readonly IAccessAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    public PermissionManagementService(
        AppDbContext dbContext,
        IAccessControlCache cache,
        IAccessAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _cache = cache;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    public async Task<PagedPermissionsDto> GetPermissionsAsync(
        string? search,
        string? group,
        bool? system,
        bool? active,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePaging(page, pageSize);
        var normalizedSearch = NormalizeFilter(search, nameof(search), 120);
        var normalizedGroup = NormalizeFilter(group, nameof(group), 120);

        var query = _dbContext.Permissions.AsNoTracking().AsQueryable();
        if (normalizedSearch is not null)
        {
            query = query.Where(permission =>
                permission.Code.Contains(normalizedSearch)
                || permission.Name.Contains(normalizedSearch)
                || (permission.Description != null && permission.Description.Contains(normalizedSearch)));
        }
        if (normalizedGroup is not null)
            query = query.Where(permission => permission.Group == normalizedGroup);
        if (system.HasValue)
            query = query.Where(permission => permission.IsSystem == system.Value);
        if (active.HasValue)
            query = query.Where(permission => permission.IsActive == active.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(permission => permission.Group)
            .ThenBy(permission => permission.SortOrder)
            .ThenBy(permission => permission.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var roleCounts = await GetRoleCountsAsync(rows.Select(permission => permission.Id), cancellationToken);
        var items = rows.Select(permission => ToDto(permission, roleCounts.GetValueOrDefault(permission.Id))).ToList();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedPermissionsDto(
            items,
            totalCount,
            page,
            pageSize,
            totalPages,
            page < totalPages,
            page > 1 && totalPages > 0);
    }

    public async Task<PermissionManagementDto> GetPermissionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var permission = await _dbContext.Permissions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Permission", id);
        var roleCount = await _dbContext.RolePermissions.AsNoTracking()
            .CountAsync(item => item.PermissionId == id, cancellationToken);
        return ToDto(permission, roleCount);
    }

    public async Task<PermissionManagementDto> CreatePermissionAsync(
        CreatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var permission = CreatePermission(request, now);
        if (await _dbContext.Permissions.AnyAsync(item => item.Code == permission.Code, cancellationToken))
            throw new ConflictException("Permission code is already in use.");

        _dbContext.Permissions.Add(permission);
        var hasAdministratorRole = await _dbContext.Roles
            .AnyAsync(role => role.Id == Role.AdminId, cancellationToken);
        if (hasAdministratorRole)
            _dbContext.RolePermissions.Add(new RolePermission(Role.AdminId, permission.Id, now));
        _auditWriter.Record("create", "Permission", permission.Id.ToString(), null, Snapshot(permission));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("Permission code is already in use.");
        }

        _cache.InvalidateAll();
        return ToDto(permission, hasAdministratorRole ? 1 : 0);
    }

    public async Task UpdatePermissionAsync(
        Guid id,
        UpdatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await _dbContext.Permissions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Permission", id);
        string requestedCode;
        try
        {
            requestedCode = Permission.NormalizeCode(request.Code);
        }
        catch (DomainException exception)
        {
            throw Validation(nameof(request.Code), exception.Message);
        }
        if (!string.Equals(permission.Code, requestedCode, StringComparison.Ordinal))
            throw Validation(nameof(request.Code), "Permission code cannot be changed.");
        if (permission.IsSystem)
            throw new ConflictException("System permissions cannot be modified.");

        var before = Snapshot(permission);
        try
        {
            permission.UpdateMetadata(
                request.Name,
                request.Description,
                request.Group,
                request.IsActive,
                request.SortOrder,
                UtcNow());
        }
        catch (DomainException exception)
        {
            throw Validation(nameof(request.Name), exception.Message);
        }

        _auditWriter.Record("update", "Permission", permission.Id.ToString(), before, Snapshot(permission));
        await _dbContext.SaveChangesAsync(cancellationToken);
        _cache.InvalidateAll();
    }

    public async Task DeactivatePermissionAsync(Guid id, CancellationToken cancellationToken)
    {
        var permission = await _dbContext.Permissions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Permission", id);
        if (permission.IsSystem)
            throw new ConflictException("System permissions cannot be deleted or disabled.");
        if (!permission.IsActive)
            return;

        var before = Snapshot(permission);
        permission.Deactivate(UtcNow());
        _auditWriter.Record("deactivate", "Permission", permission.Id.ToString(), before, Snapshot(permission));
        await _dbContext.SaveChangesAsync(cancellationToken);
        _cache.InvalidateAll();
    }

    private async Task<Dictionary<Guid, int>> GetRoleCountsAsync(
        IEnumerable<Guid> permissionIds,
        CancellationToken cancellationToken)
    {
        var ids = permissionIds.Distinct().ToArray();
        if (ids.Length == 0)
            return [];
        return await _dbContext.RolePermissions.AsNoTracking()
            .Where(item => ids.Contains(item.PermissionId))
            .GroupBy(item => item.PermissionId)
            .Select(group => new { PermissionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.PermissionId, item => item.Count, cancellationToken);
    }

    private static Permission CreatePermission(CreatePermissionRequest request, DateTime now)
    {
        try
        {
            return new Permission(
                request.Code,
                request.Name,
                request.Description,
                request.Group,
                false,
                request.IsActive,
                request.SortOrder,
                now);
        }
        catch (DomainException exception)
        {
            throw Validation(nameof(request.Code), exception.Message);
        }
    }

    private static PermissionManagementDto ToDto(Permission permission, int roleCount) => new(
        permission.Id,
        permission.Code,
        permission.Name,
        permission.Description,
        permission.Group,
        permission.IsSystem,
        permission.IsActive,
        permission.SortOrder,
        roleCount,
        permission.CreatedAt,
        permission.UpdatedAt);

    private static object Snapshot(Permission permission) => new
    {
        permission.Code,
        permission.Name,
        permission.Description,
        permission.Group,
        permission.IsSystem,
        permission.IsActive,
        permission.SortOrder
    };

    private static string? NormalizeFilter(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw Validation(field, $"{field} cannot exceed {maximumLength} characters.");
        return normalized;
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1)
            throw Validation(nameof(page), "Page must be at least 1.");
        if (pageSize is < 1 or > 100)
            throw Validation(nameof(pageSize), "Page size must be between 1 and 100.");
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
