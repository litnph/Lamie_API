using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class RoleService : IRoleService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IAccessControlCache _accessControlCache;
    private readonly IAccessAuditWriter _auditWriter;

    public RoleService(
        AppDbContext dbContext,
        TimeProvider timeProvider,
        IAccessControlCache accessControlCache,
        IAccessAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _accessControlCache = accessControlCache;
        _auditWriter = auditWriter;
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Roles.AsNoTracking();
        if (activeOnly)
            query = query.Where(role => role.IsActive);
        var roles = await query.OrderByDescending(role => role.IsSystem)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);
        return await ToDtosAsync(roles, cancellationToken);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        await _dbContext.Permissions.AsNoTracking()
            .OrderBy(permission => permission.Group)
            .ThenBy(permission => permission.Name)
            .Select(permission => new PermissionDto(
                permission.Id,
                permission.Code,
                permission.Name,
                permission.Group,
                permission.Description,
                permission.IsSystem,
                permission.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<RoleDto> GetRoleAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Role", id);
        return (await ToDtosAsync([role], cancellationToken)).Single();
    }

    public async Task<RoleDto> CreateRoleAsync(
        SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        ValidateName(request.Name);
        var code = ValidateCode(request.Code);
        var permissionIds = await ResolvePermissionIdsAsync(request.PermissionCodes, cancellationToken);
        if (await _dbContext.Roles.AnyAsync(role => role.Code == code, cancellationToken))
            throw new ConflictException("Role code is already in use.");

        var now = UtcNow();
        Role role;
        try
        {
            role = new Role(code, request.Name, request.Description, request.IsActive, now);
        }
        catch (DomainException exception)
        {
            throw Validation(nameof(request.Code), exception.Message);
        }

        _dbContext.Roles.Add(role);
        foreach (var permissionId in permissionIds)
            _dbContext.RolePermissions.Add(new RolePermission(role.Id, permissionId, now));
        _auditWriter.Record(
            "create",
            "Role",
            role.Id.ToString(),
            null,
            RoleSnapshot(role, request.PermissionCodes));
        _auditWriter.Record(
            "replace",
            "RolePermission",
            role.Id.ToString(),
            null,
            new { RoleId = role.Id, PermissionCodes = (request.PermissionCodes ?? []).Order().ToArray() });
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("Role code is already in use.");
        }
        return await GetRoleAsync(role.Id, cancellationToken);
    }

    public async Task UpdateRoleAsync(
        Guid id,
        SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        ValidateName(request.Name);
        var code = ValidateCode(request.Code);
        var role = await _dbContext.Roles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Role", id);
        if (!string.Equals(role.Code, code, StringComparison.Ordinal))
            throw Validation(nameof(request.Code), "Role code cannot be changed.");

        var permissionIds = await ResolvePermissionIdsAsync(request.PermissionCodes, cancellationToken);
        if (role.Id == Role.AdminId)
        {
            var allActiveIds = await _dbContext.Permissions.AsNoTracking()
                .Where(permission => permission.IsActive)
                .Select(permission => permission.Id)
                .ToArrayAsync(cancellationToken);
            if (allActiveIds.Except(permissionIds).Any())
                throw new ConflictException("The system administrator role must retain every permission.");
        }

        var now = UtcNow();
        var beforeRole = RoleSnapshot(role, await GetPermissionCodesAsync(role.Id, cancellationToken));
        try
        {
            role.Update(request.Name, request.Description, request.IsActive, now);
        }
        catch (DomainException exception)
        {
            throw new ConflictException(exception.Message);
        }

        var current = await _dbContext.RolePermissions
            .Where(item => item.RoleId == role.Id)
            .ToListAsync(cancellationToken);
        var selected = permissionIds.ToHashSet();
        _dbContext.RolePermissions.RemoveRange(current.Where(item => !selected.Contains(item.PermissionId)));
        var currentIds = current.Select(item => item.PermissionId).ToHashSet();
        foreach (var permissionId in selected.Where(permissionId => !currentIds.Contains(permissionId)))
            _dbContext.RolePermissions.Add(new RolePermission(role.Id, permissionId, now));

        var affectedUserIds = await RevokeRoleRefreshTokensAsync(role.Id, now, cancellationToken);
        _auditWriter.Record(
            "update",
            "Role",
            role.Id.ToString(),
            beforeRole,
            RoleSnapshot(role, request.PermissionCodes));
        _auditWriter.Record(
            "replace",
            "RolePermission",
            role.Id.ToString(),
            new { RoleId = role.Id, PermissionIds = currentIds.Order().ToArray() },
            new { RoleId = role.Id, PermissionIds = selected.Order().ToArray() });
        await _dbContext.SaveChangesAsync(cancellationToken);
        _accessControlCache.InvalidateUsers(affectedUserIds);
    }

    public async Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Role", id);
        if (role.IsSystem)
            throw new ConflictException("System roles cannot be deleted.");
        if (await _dbContext.UserRoles.AnyAsync(item => item.RoleId == id, cancellationToken))
            throw new ConflictException("The role is assigned to one or more users and cannot be deleted.");

        var grants = await _dbContext.RolePermissions.Where(item => item.RoleId == id).ToListAsync(cancellationToken);
        _auditWriter.Record(
            "delete",
            "Role",
            role.Id.ToString(),
            RoleSnapshot(role, await GetPermissionCodesAsync(role.Id, cancellationToken)),
            null);
        _dbContext.RolePermissions.RemoveRange(grants);
        _dbContext.Roles.Remove(role);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<RoleDto>> ToDtosAsync(
        IReadOnlyCollection<Role> roles,
        CancellationToken cancellationToken)
    {
        if (roles.Count == 0)
            return [];
        var ids = roles.Select(role => role.Id).ToArray();
        var counts = await _dbContext.UserRoles.AsNoTracking()
            .Where(item => ids.Contains(item.RoleId))
            .GroupBy(item => item.RoleId)
            .Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.RoleId, item => item.Count, cancellationToken);
        var grants = await (
                from rolePermission in _dbContext.RolePermissions.AsNoTracking()
                join permission in _dbContext.Permissions.AsNoTracking()
                    on rolePermission.PermissionId equals permission.Id
                where ids.Contains(rolePermission.RoleId)
                orderby permission.Code
                select new { rolePermission.RoleId, permission.Code })
            .ToListAsync(cancellationToken);
        var permissions = grants.GroupBy(item => item.RoleId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<string>)group.Select(item => item.Code).ToArray());

        return roles.Select(role => new RoleDto(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystem,
            role.IsActive,
            counts.GetValueOrDefault(role.Id),
            permissions.GetValueOrDefault(role.Id) ?? [],
            role.CreatedAt,
            role.UpdatedAt)).ToList();
    }

    private async Task<Guid[]> ResolvePermissionIdsAsync(
        IReadOnlyCollection<string>? requestedCodes,
        CancellationToken cancellationToken)
    {
        var codes = (requestedCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (codes.Length == 0)
            throw Validation(nameof(SaveRoleRequest.PermissionCodes), "Select at least one permission.");

        var rows = await _dbContext.Permissions.AsNoTracking()
            .Where(permission => codes.Contains(permission.Code) && permission.IsActive)
            .Select(permission => new { permission.Id, permission.Code })
            .ToListAsync(cancellationToken);
        var unknown = codes.Except(rows.Select(row => row.Code), StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw Validation(nameof(SaveRoleRequest.PermissionCodes), $"Unknown permissions: {string.Join(", ", unknown)}.");
        return rows.Select(row => row.Id).ToArray();
    }

    private async Task<Guid[]> RevokeRoleRefreshTokensAsync(
        Guid roleId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var userIds = await _dbContext.UserRoles.AsNoTracking()
            .Where(item => item.RoleId == roleId)
            .Select(item => item.UserId)
            .ToArrayAsync(cancellationToken);
        var tokens = await _dbContext.RefreshTokens
            .Where(token => userIds.Contains(token.UserId) && token.RevokedAt == null && token.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.Revoke(now, null);
        return userIds;
    }

    private async Task<string[]> GetPermissionCodesAsync(Guid roleId, CancellationToken cancellationToken) =>
        await (
                from rolePermission in _dbContext.RolePermissions.AsNoTracking()
                join permission in _dbContext.Permissions.AsNoTracking()
                    on rolePermission.PermissionId equals permission.Id
                where rolePermission.RoleId == roleId
                orderby permission.Code
                select permission.Code)
            .ToArrayAsync(cancellationToken);

    private static object RoleSnapshot(Role role, IEnumerable<string>? permissionCodes) => new
    {
        role.Code,
        role.Name,
        role.Description,
        role.IsSystem,
        role.IsActive,
        PermissionCodes = (permissionCodes ?? []).Order().ToArray()
    };

    private static string ValidateCode(string value)
    {
        try
        {
            return Role.NormalizeCode(value);
        }
        catch (DomainException exception)
        {
            throw Validation(nameof(SaveRoleRequest.Code), exception.Message);
        }
    }

    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 120)
            throw Validation(nameof(SaveRoleRequest.Name), "Role name is required and cannot exceed 120 characters.");
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
