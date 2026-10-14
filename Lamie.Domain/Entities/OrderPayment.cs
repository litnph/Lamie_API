using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public enum OrderPaymentKind
{
    Deposit = 1,
    Remainder = 2,
    Full = 3,
    Refund = 4
}

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Card = 3,
    Other = 99
}

public enum PaymentTransactionStatus
{
    Succeeded = 1,
    Voided = 2
}

/// <summary>
/// An immutable receipt/refund event. Order payment state is a projection of these rows;
/// legacy status flags are never used to invent a received date.
/// </summary>
public sealed class OrderPayment
{
    private OrderPayment()
    {
    }

    public OrderPayment(
        Guid orderId,
        decimal amount,
        OrderPaymentKind kind,
        PaymentMethod method,
        DateTime paidAtUtc,
        string idempotencyKey,
        string? note,
        DateTime nowUtc,
        Guid? createdById,
        bool affectsRecognizedRevenue = false,
        decimal productRevenueAdjustment = 0,
        decimal shippingRevenueAdjustment = 0)
    {
        if (orderId == Guid.Empty)
            throw new DomainException("Order is required for a payment transaction.");
        if (amount <= 0)
            throw new DomainException("Payment amount must be greater than zero.");
        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
            throw new DomainException("Payment amount cannot have more than two decimal places.");
        if (!Enum.IsDefined(kind))
            throw new DomainException("Payment kind is invalid.");
        if (!Enum.IsDefined(method))
            throw new DomainException("Payment method is invalid.");
        if (paidAtUtc.Kind != DateTimeKind.Utc || nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Payment timestamps must be UTC.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("Payment idempotency key is required.");
        if (productRevenueAdjustment < 0 || shippingRevenueAdjustment < 0
            || decimal.Round(productRevenueAdjustment, 2, MidpointRounding.AwayFromZero) != productRevenueAdjustment
            || decimal.Round(shippingRevenueAdjustment, 2, MidpointRounding.AwayFromZero) != shippingRevenueAdjustment)
            throw new DomainException("Revenue adjustments must be non-negative monetary values with at most two decimal places.");
        if (affectsRecognizedRevenue && kind != OrderPaymentKind.Refund)
            throw new DomainException("Only a refund can adjust recognized revenue.");
        if (affectsRecognizedRevenue && productRevenueAdjustment + shippingRevenueAdjustment != amount)
            throw new DomainException("Product and shipping revenue adjustments must equal the refund amount.");
        if (!affectsRecognizedRevenue && (productRevenueAdjustment != 0 || shippingRevenueAdjustment != 0))
            throw new DomainException("Revenue adjustment amounts require an explicit recognized-revenue flag.");

        var normalizedKey = idempotencyKey.Trim();
        if (normalizedKey.Length > 100)
            throw new DomainException("Payment idempotency key cannot exceed 100 characters.");

        Id = Guid.NewGuid();
        OrderId = orderId;
        Amount = amount;
        Kind = kind;
        Method = method;
        PaidAt = paidAtUtc;
        IdempotencyKey = normalizedKey;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (Note?.Length > 1000)
            throw new DomainException("Payment note cannot exceed 1000 characters.");
        Status = PaymentTransactionStatus.Succeeded;
        CreatedAt = nowUtc;
        CreatedById = createdById;
        AffectsRecognizedRevenue = affectsRecognizedRevenue;
        ProductRevenueAdjustment = productRevenueAdjustment;
        ShippingRevenueAdjustment = shippingRevenueAdjustment;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public OrderPaymentKind Kind { get; private set; }
    public PaymentMethod Method { get; private set; }
    public DateTime PaidAt { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public PaymentTransactionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public bool AffectsRecognizedRevenue { get; private set; }
    public decimal ProductRevenueAdjustment { get; private set; }
    public decimal ShippingRevenueAdjustment { get; private set; }
}
