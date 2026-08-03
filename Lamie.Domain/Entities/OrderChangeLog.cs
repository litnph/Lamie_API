namespace Lamie.Domain.Entities;

public sealed class OrderChangeLog
{
    private OrderChangeLog()
    {
    }

    internal OrderChangeLog(
        string entityName,
        string fieldName,
        string? oldValue,
        string? newValue,
        string changeType,
        Guid? changedById,
        string? changedByName,
        DateTime changedAt,
        string? note = null)
    {
        Id = Guid.NewGuid();
        EntityName = entityName;
        FieldName = fieldName;
        OldValue = oldValue;
        NewValue = newValue;
        ChangeType = changeType;
        ChangedById = changedById;
        ChangedByName = changedByName;
        ChangedAt = changedAt;
        Note = note;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string EntityName { get; private set; } = string.Empty;
    public string FieldName { get; private set; } = string.Empty;
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public string ChangeType { get; private set; } = string.Empty;
    public Guid? ChangedById { get; private set; }
    public string? ChangedByName { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public string? Note { get; private set; }
}
