using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public interface IPermissionCatalogSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken);
}

public sealed class PermissionCatalogSynchronizer : IPermissionCatalogSynchronizer
{
    private static readonly DateTime DescriptorSeededAt =
        new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _dbContext;
    private readonly IAccessControlCache _cache;

    public PermissionCatalogSynchronizer(AppDbContext dbContext, IAccessControlCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var permissions = await _dbContext.Permissions.ToListAsync(cancellationToken);
        var permissionByCode = permissions.ToDictionary(permission => permission.Code, StringComparer.Ordinal);
        for (var index = 0; index < PermissionNames.Descriptors.Count; index++)
        {
            var descriptor = PermissionNames.Descriptors[index];
            if (permissionByCode.ContainsKey(descriptor.Code))
                continue;

            var permission = new Permission(
                descriptor.Code,
                descriptor.Name,
                descriptor.Description,
                descriptor.Group,
                true,
                true,
                (index + 1) * 10,
                DescriptorSeededAt,
                PermissionNames.IdFor(descriptor.Code));
            _dbContext.Permissions.Add(permission);
            permissions.Add(permission);
            permissionByCode.Add(permission.Code, permission);
        }

        if (await _dbContext.Roles.AnyAsync(role => role.Id == Role.AdminId, cancellationToken))
        {
            var currentAdminPermissionIds = (await _dbContext.RolePermissions
                .Where(grant => grant.RoleId == Role.AdminId)
                .Select(grant => grant.PermissionId)
                .ToArrayAsync(cancellationToken)).ToHashSet();
            foreach (var permission in permissions.Where(permission => !currentAdminPermissionIds.Contains(permission.Id)))
            {
                _dbContext.RolePermissions.Add(new RolePermission(
                    Role.AdminId,
                    permission.Id,
                    DescriptorSeededAt));
            }
        }

        if (!_dbContext.ChangeTracker.HasChanges())
            return;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _cache.InvalidateAll();
    }
}

public sealed class PermissionCatalogHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PermissionCatalogHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var synchronizer = scope.ServiceProvider.GetRequiredService<IPermissionCatalogSynchronizer>();
        await synchronizer.SynchronizeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
