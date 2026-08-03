using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed class Customer
{
    private Customer()
    {
    }

    public Customer(string name, string phone, string? email, string? notes, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        Update(name, phone, email, notes, true, nowUtc);
        CreatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string NormalizedPhone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? NormalizedEmail { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Update(
        string name,
        string phone,
        string? email,
        string? notes,
        bool isActive,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Customer name is required.");
        if (name.Trim().Length > 200)
            throw new DomainException("Customer name cannot exceed 200 characters.");

        var normalizedPhone = NormalizePhone(phone);
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : User.Normalize(email);
        if (normalizedEmail?.Length > 320)
            throw new DomainException("Customer email cannot exceed 320 characters.");

        Name = name.Trim();
        Phone = phone.Trim();
        NormalizedPhone = normalizedPhone;
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        NormalizedEmail = normalizedEmail;
        Notes = NormalizeOptional(notes, 4000, "Customer notes");
        IsActive = isActive;
        UpdatedAt = nowUtc;
    }

    public void AppendNote(string note, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("Customer note is required.");

        var combined = string.IsNullOrWhiteSpace(Notes) ? note.Trim() : $"{Notes}{Environment.NewLine}{note.Trim()}";
        Notes = NormalizeOptional(combined, 4000, "Customer notes");
        UpdatedAt = nowUtc;
    }

    public static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new DomainException("Customer phone is required.");

        var normalized = new string(phone.Where(char.IsDigit).ToArray());
        if (normalized.Length is < 7 or > 20)
            throw new DomainException("Customer phone must contain between 7 and 20 digits.");

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }
}
