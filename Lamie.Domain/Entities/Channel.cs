using System.Text.RegularExpressions;
using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed partial class Channel
{
    public static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid WebsiteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid PhoneId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid WalkInId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid SocialId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private Channel()
    {
    }

    public Channel(
        string code,
        string name,
        string? iconUrl,
        int sortOrder,
        bool isActive,
        DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        Code = NormalizeCode(code);
        Update(name, iconUrl, sortOrder, isActive, nowUtc);
        CreatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? IconUrl { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsDefault =>
        Id == AdminId || Id == WebsiteId || Id == PhoneId || Id == WalkInId || Id == SocialId;

    public void Update(string name, string? iconUrl, int sortOrder, bool isActive, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Channel name is required.");
        if (name.Trim().Length > 200)
            throw new DomainException("Channel name cannot exceed 200 characters.");
        if (sortOrder < 0)
            throw new DomainException("Channel sort order cannot be negative.");

        var normalizedIconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim();
        if (normalizedIconUrl is not null &&
            (!Uri.TryCreate(normalizedIconUrl, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new DomainException("Channel icon URL must be an absolute HTTP or HTTPS URL.");
        }

        if (normalizedIconUrl?.Length > 2048)
            throw new DomainException("Channel icon URL cannot exceed 2048 characters.");

        Name = name.Trim();
        IconUrl = normalizedIconUrl;
        SortOrder = sortOrder;
        IsActive = isActive;
        UpdatedAt = nowUtc;
    }

    public void Disable(DateTime nowUtc)
    {
        IsActive = false;
        UpdatedAt = nowUtc;
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Channel code is required.");

        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.Length > 50)
            throw new DomainException("Channel code cannot exceed 50 characters.");
        if (!CodePattern().IsMatch(normalized))
            throw new DomainException("Channel code may contain lowercase letters, numbers, and single hyphens only.");

        return normalized;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
