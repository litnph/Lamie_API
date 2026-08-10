using System.Security.Claims;
using System.Text.Json;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;

namespace Lamie.API.Services;

public interface IAccessAuditWriter
{
    void Record(string action, string entityType, string entityId, object? before, object? after);
}

public sealed class AccessAuditWriter : IAccessAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public AccessAuditWriter(
        AppDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public void Record(string action, string entityType, string entityId, object? before, object? after)
    {
        _dbContext.AccessAudits.Add(new AccessAudit(
            GetActorUserId(),
            action,
            entityType,
            entityId,
            Serialize(before),
            Serialize(after),
            _timeProvider.GetUtcNow().UtcDateTime));
    }

    private Guid? GetActorUserId()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var value = principal?.FindFirstValue("sub") ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}
