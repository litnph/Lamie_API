using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed class Role
{
    public static readonly Guid AdminId = Guid.Parse("20000000-0000-4000-8000-000000000001");
    public static readonly Guid ManagerId = Guid.Parse("20000000-0000-4000-8000-000000000002");
    public static readonly Guid StaffId = Guid.Parse("20000000-0000-4000-8000-000000000003");

    private Role()
    {
    }

    public Role(string code, string name, string? description, bool isActive, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        Code = NormalizeCode(code);
        Name = Required(name, "Role name");
        Description = Optional(description);
        IsSystem = false;
        IsActive = isActive;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Update(string name, string? description, bool isActive, DateTime nowUtc)
    {
        if (Id == AdminId && !isActive)
            throw new DomainException("The system administrator role cannot be disabled");

        Name = Required(name, "Role name");
        Description = Optional(description);
        IsActive = isActive;
        UpdatedAt = nowUtc;
    }

    public static Guid IdFor(BuiltInRole role) => role switch
    {
        BuiltInRole.Admin => AdminId,
        BuiltInRole.Manager => ManagerId,
        BuiltInRole.Staff => StaffId,
        _ => throw new DomainException("Role is invalid")
    };

    public static BuiltInRole LegacyValueFor(Guid roleId) => roleId switch
    {
        var id when id == AdminId => BuiltInRole.Admin,
        var id when id == ManagerId => BuiltInRole.Manager,
        _ => BuiltInRole.Staff
    };

    public static string NormalizeCode(string value)
    {
        var code = Required(value, "Role code").ToLowerInvariant();
        if (code.Length > 80 || code.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new DomainException("Role code may contain only letters, numbers, dots, hyphens, or underscores and cannot exceed 80 characters");
        return code;
    }

    private static string Required(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{label} is required");
        return value.Trim();
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class Permission
{
    private Permission()
    {
    }

    public Permission(
        string code,
        string name,
        string? description,
        string group,
        bool isSystem,
        bool isActive,
        int sortOrder,
        DateTime nowUtc,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Code = NormalizeCode(code);
        Name = Required(name, "Permission name", 160);
        Description = Optional(description, 500);
        Group = Required(group, "Permission group", 120);
        IsSystem = isSystem;
        IsActive = isActive;
        SortOrder = NonNegative(sortOrder);
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Group { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void UpdateMetadata(
        string name,
        string? description,
        string group,
        bool isActive,
        int sortOrder,
        DateTime nowUtc)
    {
        if (IsSystem)
            throw new DomainException("System permissions cannot be modified");

        Name = Required(name, "Permission name", 160);
        Description = Optional(description, 500);
        Group = Required(group, "Permission group", 120);
        IsActive = isActive;
        SortOrder = NonNegative(sortOrder);
        UpdatedAt = nowUtc;
    }

    public void Deactivate(DateTime nowUtc)
    {
        if (IsSystem)
            throw new DomainException("System permissions cannot be disabled");
        IsActive = false;
        UpdatedAt = nowUtc;
    }

    public static string NormalizeCode(string value)
    {
        var code = Required(value, "Permission code", 120).ToLowerInvariant();
        if (code.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new DomainException("Permission code may contain only letters, numbers, dots, hyphens, or underscores");
        return code;
    }

    private static string Required(string? value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{label} is required");
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{label} cannot exceed {maximumLength} characters");
        return normalized;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"Permission description cannot exceed {maximumLength} characters");
        return normalized;
    }

    private static int NonNegative(int value)
    {
        if (value < 0)
            throw new DomainException("Permission sort order cannot be negative");
        return value;
    }
}

public sealed class AdminNavigation
{
    private AdminNavigation()
    {
    }

    public AdminNavigation(
        string key,
        Guid? parentId,
        string? moduleKey,
        string? pageKey,
        string label,
        string? description,
        string? path,
        string? iconKey,
        string? permissionCode,
        int sortOrder,
        bool isVisible,
        bool isEnabled,
        bool isSystem,
        bool openInNewTab,
        DateTime nowUtc,
        Guid? actorUserId,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Key = NormalizeIdentifier(key, "Navigation key", 120);
        Apply(
            parentId,
            moduleKey,
            pageKey,
            label,
            description,
            path,
            iconKey,
            permissionCode,
            sortOrder,
            isVisible,
            isEnabled,
            openInNewTab,
            nowUtc,
            actorUserId);
        IsSystem = isSystem;
        CreatedAt = nowUtc;
        CreatedBy = actorUserId;
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public Guid? ParentId { get; private set; }
    public string? ModuleKey { get; private set; }
    public string? PageKey { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Path { get; private set; }
    public string? IconKey { get; private set; }
    public string? PermissionCode { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsVisible { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool IsSystem { get; private set; }
    public bool OpenInNewTab { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Update(
        Guid? parentId,
        string? moduleKey,
        string? pageKey,
        string label,
        string? description,
        string? path,
        string? iconKey,
        string? permissionCode,
        int sortOrder,
        bool isVisible,
        bool isEnabled,
        bool openInNewTab,
        DateTime nowUtc,
        Guid? actorUserId) =>
        Apply(
            parentId,
            moduleKey,
            pageKey,
            label,
            description,
            path,
            iconKey,
            permissionCode,
            sortOrder,
            isVisible,
            isEnabled,
            openInNewTab,
            nowUtc,
            actorUserId);

    public void Move(Guid? parentId, int sortOrder, DateTime nowUtc, Guid? actorUserId)
    {
        ParentId = parentId;
        SortOrder = NonNegative(sortOrder, "Navigation sort order");
        UpdatedAt = nowUtc;
        UpdatedBy = actorUserId;
    }

    public void SetEnabled(bool isEnabled, DateTime nowUtc, Guid? actorUserId)
    {
        if (IsEnabled == isEnabled)
            return;
        IsEnabled = isEnabled;
        UpdatedAt = nowUtc;
        UpdatedBy = actorUserId;
    }

    public static string NormalizeKey(string value) =>
        NormalizeIdentifier(value, "Navigation key", 120);

    private void Apply(
        Guid? parentId,
        string? moduleKey,
        string? pageKey,
        string label,
        string? description,
        string? path,
        string? iconKey,
        string? permissionCode,
        int sortOrder,
        bool isVisible,
        bool isEnabled,
        bool openInNewTab,
        DateTime nowUtc,
        Guid? actorUserId)
    {
        var normalizedModuleKey = OptionalIdentifier(moduleKey, "Module key", 120);
        var normalizedPageKey = OptionalIdentifier(pageKey, "Page key", 160);
        var normalizedPath = NormalizePath(path, isVisible);
        var hasRoute = normalizedPath is not null
            || normalizedModuleKey is not null
            || normalizedPageKey is not null;
        if (hasRoute && (normalizedPath is null || normalizedModuleKey is null || normalizedPageKey is null))
            throw new DomainException("Path, module key, and page key must be supplied together");
        if (openInNewTab)
            throw new DomainException("Internal administration navigation cannot open in a new tab");

        ParentId = parentId;
        ModuleKey = normalizedModuleKey;
        PageKey = normalizedPageKey;
        Label = Required(label, "Navigation label", 160);
        Description = Optional(description, 500, "Navigation description");
        Path = normalizedPath;
        IconKey = OptionalIdentifier(iconKey, "Icon key", 80);
        PermissionCode = string.IsNullOrWhiteSpace(permissionCode)
            ? null
            : Permission.NormalizeCode(permissionCode);
        SortOrder = NonNegative(sortOrder, "Navigation sort order");
        IsVisible = isVisible;
        IsEnabled = isEnabled;
        OpenInNewTab = openInNewTab;
        UpdatedAt = nowUtc;
        UpdatedBy = actorUserId;
    }

    private static string? NormalizePath(string? value, bool isVisible)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var path = value.Trim();
        if (path.Length > 400)
            throw new DomainException("Navigation path cannot exceed 400 characters");
        if (!path.StartsWith("/admin/", StringComparison.Ordinal)
            || path.Contains('?', StringComparison.Ordinal)
            || path.Contains('#', StringComparison.Ordinal)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Any(char.IsControl))
        {
            throw new DomainException("Navigation path must be a safe internal /admin/ path");
        }

        var segments = path.Split('/');
        if (segments.Length < 3 || segments.Skip(1).Any(string.IsNullOrWhiteSpace))
            throw new DomainException("Navigation path contains an empty segment");
        foreach (var segment in segments.Skip(1))
        {
            if (segment is "." or "..")
                throw new DomainException("Navigation path cannot contain traversal segments");
            if (segment.StartsWith(':'))
            {
                if (isVisible)
                    throw new DomainException("Visible navigation paths cannot contain route parameters");
                var parameter = segment[1..];
                if (parameter.Length == 0 || !IsRouteIdentifier(parameter))
                    throw new DomainException("Navigation route parameter is invalid");
                continue;
            }
            if (segment.Any(character =>
                    !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '~')))
            {
                throw new DomainException("Navigation path contains an unsupported character");
            }
        }
        return path;
    }

    private static bool IsRouteIdentifier(string value) =>
        (char.IsLetter(value[0]) || value[0] == '_')
        && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static string NormalizeIdentifier(string value, string label, int maximumLength)
    {
        var normalized = Required(value, label, maximumLength).ToLowerInvariant();
        if (normalized.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new DomainException($"{label} may contain only letters, numbers, dots, hyphens, or underscores");
        }
        return normalized;
    }

    private static string? OptionalIdentifier(string? value, string label, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeIdentifier(value, label, maximumLength);

    private static string Required(string? value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{label} is required");
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{label} cannot exceed {maximumLength} characters");
        return normalized;
    }

    private static string? Optional(string? value, int maximumLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{label} cannot exceed {maximumLength} characters");
        return normalized;
    }

    private static int NonNegative(int value, string label)
    {
        if (value < 0)
            throw new DomainException($"{label} cannot be negative");
        return value;
    }
}

public sealed class AccessAudit
{
    private AccessAudit()
    {
    }

    public AccessAudit(
        Guid? actorUserId,
        string action,
        string entityType,
        string entityId,
        string? beforeJson,
        string? afterJson,
        DateTime occurredAt)
    {
        Id = Guid.NewGuid();
        ActorUserId = actorUserId;
        Action = Required(action, nameof(action), 80);
        EntityType = Required(entityType, nameof(entityType), 80);
        EntityId = Required(entityId, nameof(entityId), 160);
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }

    private static string Required(string? value, string label, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{label} is required");
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{label} cannot exceed {maximumLength} characters");
        return normalized;
    }
}

public sealed class UserRole
{
    private UserRole()
    {
    }

    public UserRole(Guid userId, Guid roleId, DateTime assignedAt)
    {
        if (userId == Guid.Empty || roleId == Guid.Empty)
            throw new DomainException("User and role are required");
        UserId = userId;
        RoleId = roleId;
        AssignedAt = assignedAt;
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTime AssignedAt { get; private set; }
}

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    public RolePermission(Guid roleId, Guid permissionId, DateTime grantedAt)
    {
        if (roleId == Guid.Empty || permissionId == Guid.Empty)
            throw new DomainException("Role and permission are required");
        RoleId = roleId;
        PermissionId = permissionId;
        GrantedAt = grantedAt;
    }

    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public DateTime GrantedAt { get; private set; }
}
