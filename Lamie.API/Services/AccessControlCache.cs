using System.Collections.Concurrent;
using Lamie.Application.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Lamie.API.Services;

public sealed record UserAuthorizationSnapshot(
    Guid RoleId,
    string RoleCode,
    string RoleName,
    IReadOnlyCollection<string> PermissionCodes);

public interface IAccessControlCache
{
    bool TryGetAuthorization(Guid userId, out UserAuthorizationSnapshot? snapshot);
    void SetAuthorization(Guid userId, UserAuthorizationSnapshot snapshot);
    bool TryGetNavigation(Guid userId, out IReadOnlyList<CurrentNavigationItemDto>? navigation);
    void SetNavigation(Guid userId, IReadOnlyList<CurrentNavigationItemDto> navigation);
    bool TryGetRoutes(Guid userId, out IReadOnlyList<CurrentNavigationRouteDto>? routes);
    void SetRoutes(Guid userId, IReadOnlyList<CurrentNavigationRouteDto> routes);
    void InvalidateUser(Guid userId);
    void InvalidateUsers(IEnumerable<Guid> userIds);
    void InvalidateNavigation();
    void InvalidateAll();
}

public sealed class AccessControlCache : IAccessControlCache
{
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SlidingLifetime = TimeSpan.FromMinutes(1);
    private readonly IMemoryCache _memoryCache;
    private readonly ConcurrentDictionary<Guid, byte> _authorizationUsers = new();
    private readonly ConcurrentDictionary<Guid, byte> _navigationUsers = new();

    public AccessControlCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public bool TryGetAuthorization(Guid userId, out UserAuthorizationSnapshot? snapshot) =>
        _memoryCache.TryGetValue(AuthorizationKey(userId), out snapshot);

    public void SetAuthorization(Guid userId, UserAuthorizationSnapshot snapshot)
    {
        _authorizationUsers[userId] = 0;
        _memoryCache.Set(
            AuthorizationKey(userId),
            snapshot,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = AbsoluteLifetime,
                SlidingExpiration = SlidingLifetime
            });
    }

    public bool TryGetNavigation(
        Guid userId,
        out IReadOnlyList<CurrentNavigationItemDto>? navigation) =>
        _memoryCache.TryGetValue(NavigationKey(userId), out navigation);

    public void SetNavigation(Guid userId, IReadOnlyList<CurrentNavigationItemDto> navigation)
    {
        _navigationUsers[userId] = 0;
        Set(NavigationKey(userId), navigation);
    }

    public bool TryGetRoutes(Guid userId, out IReadOnlyList<CurrentNavigationRouteDto>? routes) =>
        _memoryCache.TryGetValue(RoutesKey(userId), out routes);

    public void SetRoutes(Guid userId, IReadOnlyList<CurrentNavigationRouteDto> routes)
    {
        _navigationUsers[userId] = 0;
        Set(RoutesKey(userId), routes);
    }

    public void InvalidateUser(Guid userId)
    {
        _memoryCache.Remove(AuthorizationKey(userId));
        _memoryCache.Remove(NavigationKey(userId));
        _memoryCache.Remove(RoutesKey(userId));
        _authorizationUsers.TryRemove(userId, out _);
        _navigationUsers.TryRemove(userId, out _);
    }

    public void InvalidateUsers(IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds.Distinct())
            InvalidateUser(userId);
    }

    public void InvalidateAll()
    {
        foreach (var userId in _authorizationUsers.Keys)
            InvalidateUser(userId);
        InvalidateNavigation();
    }

    public void InvalidateNavigation()
    {
        foreach (var userId in _navigationUsers.Keys)
        {
            _memoryCache.Remove(NavigationKey(userId));
            _memoryCache.Remove(RoutesKey(userId));
            _navigationUsers.TryRemove(userId, out _);
        }
    }

    private void Set<TValue>(string key, TValue value) =>
        _memoryCache.Set(
            key,
            value,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = AbsoluteLifetime,
                SlidingExpiration = SlidingLifetime
            });

    private static string AuthorizationKey(Guid userId) => $"access-control:authorization:{userId:N}";
    private static string NavigationKey(Guid userId) => $"access-control:navigation:{userId:N}";
    private static string RoutesKey(Guid userId) => $"access-control:routes:{userId:N}";
}
