using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed class Expense
{
    public const decimal MaximumAmount = 9_999_999_999_999_999.99m;

    private Expense()
    {
    }

    public Expense(
        Guid expenseCategoryId,
        DateOnly expenseDate,
        decimal amount,
        string description,
        string? notes,
        DateTime nowUtc,
        Guid? stockReceiptId = null,
        DateTime? paidAtUtc = null,
        PaymentMethod? paymentMethod = null,
        string? vendor = null,
        string? paymentIdempotencyKey = null)
    {
        Id = Guid.NewGuid();
        StockReceiptId = stockReceiptId;
        Update(expenseCategoryId, expenseDate, amount, description, notes, nowUtc,
            paidAtUtc, paymentMethod, vendor, paymentIdempotencyKey);
        CreatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid ExpenseCategoryId { get; private set; }
    public DateOnly ExpenseDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public Guid? StockReceiptId { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public PaymentMethod? PaymentMethod { get; private set; }
    public string? Vendor { get; private set; }
    public string? PaymentIdempotencyKey { get; private set; }
    public bool PaymentHistoryIncomplete { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Update(
        Guid expenseCategoryId,
        DateOnly expenseDate,
        decimal amount,
        string description,
        string? notes,
        DateTime nowUtc,
        DateTime? paidAtUtc = null,
        PaymentMethod? paymentMethod = null,
        string? vendor = null,
        string? paymentIdempotencyKey = null)
    {
        if (expenseCategoryId == Guid.Empty)
            throw new DomainException("Expense category is required.");
        if (expenseDate == default)
            throw new DomainException("Expense date is required.");
        if (amount <= 0)
            throw new DomainException("Expense amount must be greater than zero.");
        if (amount > MaximumAmount)
            throw new DomainException($"Expense amount cannot exceed {MaximumAmount}.");
        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
            throw new DomainException("Expense amount cannot have more than two decimal places.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Expense description is required.");
        if (description.Trim().Length > 500)
            throw new DomainException("Expense description cannot exceed 500 characters.");
        if (paidAtUtc.HasValue && paidAtUtc.Value.Kind != DateTimeKind.Utc)
            throw new DomainException("Expense paid time must be UTC.");
        if (paidAtUtc.HasValue != paymentMethod.HasValue)
            throw new DomainException("Paid time and payment method must be supplied together.");
        if (paymentMethod.HasValue && !Enum.IsDefined(paymentMethod.Value))
            throw new DomainException("Expense payment method is invalid.");
        if (!string.IsNullOrWhiteSpace(paymentIdempotencyKey) && !paidAtUtc.HasValue)
            throw new DomainException("Expense payment key requires a paid time.");

        ExpenseCategoryId = expenseCategoryId;
        ExpenseDate = expenseDate;
        Amount = amount;
        Description = description.Trim();
        Notes = NormalizeOptional(notes, 2000, "Expense notes");
        var normalizedPaymentKey = NormalizeOptional(paymentIdempotencyKey, 100, "Expense payment idempotency key")
            ?? (paidAtUtc.HasValue ? PaymentIdempotencyKey : null);
        PaidAt = paidAtUtc;
        PaymentMethod = paymentMethod;
        Vendor = NormalizeOptional(vendor, 200, "Expense vendor");
        PaymentIdempotencyKey = normalizedPaymentKey;
        if (paidAtUtc.HasValue)
            PaymentHistoryIncomplete = false;
        UpdatedAt = nowUtc;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }
}
