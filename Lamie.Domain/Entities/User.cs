using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public enum UserRole
{
    Admin = 1,
    Manager = 2,
    Staff = 3
}

public enum UserStatus
{
    Inactive = 0,
    Active = 1,
    Locked = 2
}

public sealed class User
{
    private User()
    {
    }

    public User(
        string email,
        string userName,
        string passwordHash,
        string fullName,
        string? phone,
        UserRole role,
        bool isActive,
        DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        SetIdentity(email, userName);
        SetPasswordHash(passwordHash);
        UpdateProfile(fullName, phone, role, isActive, nowUtc);
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string UserName { get; private set; } = string.Empty;
    public string NormalizedUserName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }
    public int AccessFailedCount { get; private set; }
    public DateTime? LockoutEnd { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    public bool CanAttemptLogin(DateTime nowUtc)
    {
        if (Status == UserStatus.Locked && LockoutEnd <= nowUtc)
        {
            Status = UserStatus.Active;
            LockoutEnd = null;
            AccessFailedCount = 0;
            UpdatedAt = nowUtc;
        }

        return Status == UserStatus.Active;
    }

    public void RecordFailedLogin(DateTime nowUtc)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= 5)
        {
            Status = UserStatus.Locked;
            LockoutEnd = nowUtc.AddMinutes(15);
        }

        UpdatedAt = nowUtc;
    }

    public void RecordSuccessfulLogin(DateTime nowUtc)
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
        LastLoginAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void UpdateProfile(string fullName, string? phone, UserRole role, bool isActive, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Full name is required");

        if (!Enum.IsDefined(role))
            throw new DomainException("Role is invalid");

        FullName = fullName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Role = role;
        Status = isActive ? UserStatus.Active : UserStatus.Inactive;
        if (!isActive)
        {
            AccessFailedCount = 0;
            LockoutEnd = null;
        }

        UpdatedAt = nowUtc;
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Password hash is required");

        PasswordHash = passwordHash;
    }

    private void SetIdentity(string email, string userName)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required");
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("Username is required");

        Email = email.Trim();
        NormalizedEmail = Normalize(email);
        UserName = userName.Trim();
        NormalizedUserName = Normalize(userName);
    }
}
