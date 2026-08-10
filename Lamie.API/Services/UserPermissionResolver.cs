using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public interface IUserPermissionResolver
{
    Task<UserAuthorizationSnapshot> ResolveAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class UserPermissionResolver : IUserPermissionResolver
{
    private readonly AppDbContext _dbContext;
    private readonly IAccessControlCache _cache;

    public UserPermissionResolver(AppDbContext dbContext, IAccessControlCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<UserAuthorizationSnapshot> ResolveAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetAuthorization(userId, out var cached) && cached is not null)
            return cached;

        var user = await _dbContext.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.Role, candidate.Status })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedException();
        if (user.Status != UserStatus.Active)
            throw new UnauthorizedException();

        var role = await (
                from userRole in _dbContext.UserRoles.AsNoTracking()
                join candidate in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals candidate.Id
                where userRole.UserId == userId
                select candidate)
            .SingleOrDefaultAsync(cancellationToken);

        if (role is null)
        {
            var fallbackId = Role.IdFor(user.Role);
            role = await _dbContext.Roles.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == fallbackId, cancellationToken);
        }

        if (role is null || !role.IsActive)
            throw new UnauthorizedException("The account has no active role.");

        var permissions = await (
                from rolePermission in _dbContext.RolePermissions.AsNoTracking()
                join permission in _dbContext.Permissions.AsNoTracking()
                    on rolePermission.PermissionId equals permission.Id
                where rolePermission.RoleId == role.Id && permission.IsActive
                orderby permission.Code
                select permission.Code)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var snapshot = new UserAuthorizationSnapshot(role.Id, role.Code, role.Name, permissions);
        _cache.SetAuthorization(userId, snapshot);
        return snapshot;
    }
}
