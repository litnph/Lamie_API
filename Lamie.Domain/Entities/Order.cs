using System.Globalization;
using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public enum OrderStatus
{
    Created = 1,
    Producing = 2,
    Shipping = 3,
    Completed = 4,
    Cancelled = 99
}

public enum PaymentStatus
{
    Unpaid = 1,
    Deposited = 2,
    Paid = 3
}

public sealed record OrderDetails(
    string OrdererName,
    string OrdererPhone,
    string RecipientName,
    string RecipientPhone,
    bool PickupAtShop,
    bool ProvinceShipping,
    string? DeliveryAddress,
    string? DeliveryAddressDescription,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    DateTime DeliveryAtUtc,
    DateTime? DeliveryToUtc,
    decimal DepositAmount,
    decimal ShippingFee,
    decimal? ShippingFeeActual,
    string? Description,
    string? ContentNote,
    AdministrativeScheme? AddressScheme = null,
    string? ProvinceCode = null,
    string? ProvinceName = null,
    string? DistrictCode = null,
    string? DistrictName = null,
    string? CommuneCode = null,
    string? CommuneName = null,
    string? AddressDetail = null,
    string? FullAddressSnapshot = null);

public sealed class Order
{
    private static readonly IReadOnlyDictionary<OrderStatus, IReadOnlySet<OrderStatus>> AllowedTransitions =
        new Dictionary<OrderStatus, IReadOnlySet<OrderStatus>>
        {
            [OrderStatus.Created] = new HashSet<OrderStatus> { OrderStatus.Producing, OrderStatus.Cancelled },
            [OrderStatus.Producing] = new HashSet<OrderStatus> { OrderStatus.Shipping, OrderStatus.Cancelled },
            [OrderStatus.Shipping] = new HashSet<OrderStatus> { OrderStatus.Completed, OrderStatus.Cancelled },
            [OrderStatus.Completed] = new HashSet<OrderStatus>(),
            [OrderStatus.Cancelled] = new HashSet<OrderStatus>()
        };

    private readonly List<OrderItem> _items = new();
    private readonly List<OrderImage> _images = new();
    private readonly List<OrderChangeLog> _changeLogs = new();

    private Order()
    {
    }

    public Order(
        string orderCode,
        Guid channelId,
        Guid? customerId,
        OrderDetails details,
        IEnumerable<OrderItemSnapshot> items,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
            throw new DomainException("Order code is required.");
        if (orderCode.Trim().Length > 40)
            throw new DomainException("Order code cannot exceed 40 characters.");
        if (channelId == Guid.Empty)
            throw new DomainException("Order channel is required.");
        EnsureUtc(nowUtc, "Current time");

        Id = Guid.NewGuid();
        OrderCode = orderCode.Trim();
        ChannelId = channelId;
        CustomerId = customerId;
        OrderStatus = OrderStatus.Created;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
        CreatedById = actorId;
        UpdatedById = actorId;
        ApplyDetails(details);
        InitializeItems(items);
        PaymentStatus = DepositAmount > 0 ? PaymentStatus.Deposited : PaymentStatus.Unpaid;
        AddChangeLog("Order", "OrderStatus", null, EnumValue(OrderStatus), "Created", actorId, actorName, nowUtc);
    }

    public Guid Id { get; private set; }
    public string OrderCode { get; private set; } = string.Empty;
    public Guid? CustomerId { get; private set; }
    public Guid ChannelId { get; private set; }
    public string OrdererName { get; private set; } = string.Empty;
    public string OrdererPhone { get; private set; } = string.Empty;
    public string RecipientName { get; private set; } = string.Empty;
    public string RecipientPhone { get; private set; } = string.Empty;
    public bool PickupAtShop { get; private set; }
    public bool ProvinceShipping { get; private set; }
    public string? DeliveryAddress { get; private set; }
    public string? DeliveryAddressDescription { get; private set; }
    public AdministrativeScheme? AddressScheme { get; private set; }
    public string? ProvinceCode { get; private set; }
    public string? ProvinceName { get; private set; }
    public string? DistrictCode { get; private set; }
    public string? DistrictName { get; private set; }
    public string? CommuneCode { get; private set; }
    public string? CommuneName { get; private set; }
    public string? AddressDetail { get; private set; }
    public string? FullAddressSnapshot { get; private set; }
    public decimal? DeliveryLatitude { get; private set; }
    public decimal? DeliveryLongitude { get; private set; }
    public DateTime DeliveryAt { get; private set; }
    public DateTime? DeliveryTo { get; private set; }
    public decimal DepositAmount { get; private set; }
    public decimal ShippingFee { get; private set; }
    public decimal? ShippingFeeActual { get; private set; }
    public decimal SubTotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TotalAmount { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public OrderStatus OrderStatus { get; private set; }
    public string? Description { get; private set; }
    public string? ContentNote { get; private set; }
    public bool InventoryReserved { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public IReadOnlyCollection<OrderItem> Items => _items;
    public IReadOnlyCollection<OrderImage> Images => _images;
    public IReadOnlyCollection<OrderChangeLog> ChangeLogs => _changeLogs;

    public bool RequiresInventoryReservation(OrderStatus target) =>
        OrderStatus == OrderStatus.Created && target == OrderStatus.Producing && !InventoryReserved;

    public bool RequiresInventoryRestore(OrderStatus target) =>
        target == OrderStatus.Cancelled && InventoryReserved;

    public IReadOnlyList<string> Update(
        Guid channelId,
        Guid? customerId,
        OrderDetails details,
        IEnumerable<OrderItemUpdate> items,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName)
    {
        if (OrderStatus is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new DomainException("Không thể chỉnh sửa đơn hàng đã hoàn tất hoặc đã hủy.");
        if (OrderStatus != OrderStatus.Created)
            throw new DomainException("Đơn hàng chỉ có thể chỉnh sửa khi ở trạng thái Đã tạo.");
        if (InventoryReserved)
            throw new DomainException("Đơn hàng Đã tạo có trạng thái giữ tồn kho không nhất quán. Vui lòng tải lại và liên hệ quản trị viên.");
        if (channelId == Guid.Empty)
            throw new DomainException("Order channel is required.");
        EnsureUtc(nowUtc, "Current time");

        var requestedItems = ValidateItemUpdates(items);

        ChannelId = channelId;
        CustomerId = customerId;
        var previousPaymentStatus = PaymentStatus;
        ApplyDetails(details);
        var removedImageUrls = SynchronizeItems(requestedItems);
        if (PaymentStatus != PaymentStatus.Paid)
        {
            PaymentStatus = DepositAmount > 0 ? PaymentStatus.Deposited : PaymentStatus.Unpaid;
            if (PaymentStatus != previousPaymentStatus)
            {
                AddChangeLog(
                    "Order",
                    "PaymentStatus",
                    EnumValue(previousPaymentStatus),
                    EnumValue(PaymentStatus),
                    "PaymentChanged",
                    actorId,
                    actorName,
                    nowUtc,
                    "Payment state synchronized with the edited deposit amount.");
            }
        }
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "Details", null, null, "Updated", actorId, actorName, nowUtc);
        return removedImageUrls;
    }

    public void AddImage(Guid orderItemId, string imageUrl, int sortOrder, string? description = null)
    {
        if (_items.All(item => item.Id != orderItemId))
            throw new DomainException("Order image must reference an item in this order.");
        _images.Add(new OrderImage(orderItemId, imageUrl, sortOrder, description));
    }

    public void ChangeStatus(
        OrderStatus target,
        bool inventoryReservedAfterTransition,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName,
        string? note = null)
    {
        EnsureUtc(nowUtc, "Current time");
        if (!Enum.IsDefined(target) || !AllowedTransitions[OrderStatus].Contains(target))
            throw new DomainException($"Order status cannot transition from {OrderStatus} to {target}.");

        var previous = OrderStatus;
        if (RequiresInventoryReservation(target))
            InventoryReserved = inventoryReservedAfterTransition;
        else if (RequiresInventoryRestore(target))
            InventoryReserved = false;

        OrderStatus = target;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        if (target == OrderStatus.Completed)
            CompletedAt = nowUtc;
        if (target == OrderStatus.Cancelled)
            CancelledAt = nowUtc;

        AddChangeLog("Order", "OrderStatus", EnumValue(previous), EnumValue(target), "StatusChanged", actorId, actorName, nowUtc, note);
    }

    public void ChangePaymentStatus(
        PaymentStatus target,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName,
        string? note = null)
    {
        EnsureUtc(nowUtc, "Current time");
        var valid = PaymentStatus switch
        {
            PaymentStatus.Unpaid => target is PaymentStatus.Deposited or PaymentStatus.Paid,
            PaymentStatus.Deposited => target == PaymentStatus.Paid,
            _ => false
        };
        if (!Enum.IsDefined(target) || !valid)
            throw new DomainException($"Payment status cannot transition from {PaymentStatus} to {target}.");

        var previous = PaymentStatus;
        PaymentStatus = target;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "PaymentStatus", EnumValue(previous), EnumValue(target), "PaymentChanged", actorId, actorName, nowUtc, note);
    }

    private void ApplyDetails(OrderDetails details)
    {
        OrdererName = Required(details.OrdererName, 200, "Orderer name");
        OrdererPhone = Optional(details.OrdererPhone, 30, "Orderer phone") ?? string.Empty;
        RecipientName = Required(details.RecipientName, 200, "Recipient name");
        RecipientPhone = Required(details.RecipientPhone, 30, "Recipient phone");
        EnsureUtc(details.DeliveryAtUtc, "Delivery time");
        if (details.DeliveryAtUtc == default)
            throw new DomainException("Delivery time is required.");
        if (details.DeliveryToUtc.HasValue)
        {
            EnsureUtc(details.DeliveryToUtc.Value, "Delivery end time");
            if (details.DeliveryToUtc.Value < details.DeliveryAtUtc)
                throw new DomainException("Delivery end time cannot be before delivery start time.");
        }
        if (details.DepositAmount < 0 || details.ShippingFee < 0 || details.ShippingFeeActual < 0)
            throw new DomainException("Order monetary values cannot be negative.");
        if (details.PickupAtShop && details.ProvinceShipping)
            throw new DomainException("An order cannot be both pickup and province shipping.");

        if ((details.DeliveryLatitude.HasValue && !details.DeliveryLongitude.HasValue) ||
            (!details.DeliveryLatitude.HasValue && details.DeliveryLongitude.HasValue))
            throw new DomainException("Delivery latitude and longitude must be supplied together.");
        if (details.DeliveryLatitude is < -90 or > 90)
            throw new DomainException("Delivery latitude must be between -90 and 90.");
        if (details.DeliveryLongitude is < -180 or > 180)
            throw new DomainException("Delivery longitude must be between -180 and 180.");

        PickupAtShop = details.PickupAtShop;
        ProvinceShipping = details.ProvinceShipping;
        if (details.AddressScheme.HasValue && !Enum.IsDefined(details.AddressScheme.Value))
            throw new DomainException("Address scheme is invalid.");
        if (details.AddressScheme == AdministrativeScheme.Current
            && (!string.IsNullOrWhiteSpace(details.DistrictCode) || !string.IsNullOrWhiteSpace(details.DistrictName)))
            throw new DomainException("A current administrative address cannot contain a district.");

        AddressScheme = details.PickupAtShop ? null : details.AddressScheme;
        ProvinceCode = details.PickupAtShop ? null : Optional(details.ProvinceCode, 10, "Province code");
        ProvinceName = details.PickupAtShop ? null : Optional(details.ProvinceName, 200, "Province name");
        DistrictCode = details.PickupAtShop ? null : Optional(details.DistrictCode, 10, "District code");
        DistrictName = details.PickupAtShop ? null : Optional(details.DistrictName, 200, "District name");
        CommuneCode = details.PickupAtShop ? null : Optional(details.CommuneCode, 10, "Commune code");
        CommuneName = details.PickupAtShop ? null : Optional(details.CommuneName, 200, "Commune name");
        AddressDetail = details.PickupAtShop ? null : Optional(details.AddressDetail, 1000, "Address detail");
        FullAddressSnapshot = details.PickupAtShop
            ? null
            : Optional(details.FullAddressSnapshot, 1500, "Full address snapshot");
        DeliveryAddress = details.PickupAtShop
            ? null
            : Optional(details.FullAddressSnapshot ?? details.DeliveryAddress, 1500, "Delivery address");
        DeliveryAddressDescription = details.PickupAtShop
            ? null
            : Optional(details.DeliveryAddressDescription, 1000, "Delivery address description");
        DeliveryLatitude = details.PickupAtShop ? null : details.DeliveryLatitude;
        DeliveryLongitude = details.PickupAtShop ? null : details.DeliveryLongitude;
        DeliveryAt = details.DeliveryAtUtc;
        DeliveryTo = details.DeliveryToUtc;
        DepositAmount = Money(details.DepositAmount);
        ShippingFee = details.PickupAtShop ? 0 : Money(details.ShippingFee);
        ShippingFeeActual = details.PickupAtShop || !details.ShippingFeeActual.HasValue
            ? null
            : Money(details.ShippingFeeActual.Value);
        Description = Optional(details.Description, 4000, "Order description");
        ContentNote = Optional(details.ContentNote, 4000, "Order content note");
    }

    private void InitializeItems(IEnumerable<OrderItemSnapshot> snapshots)
    {
        var requested = snapshots.ToList();
        if (requested.Count == 0)
            throw new DomainException("Order must contain at least one item.");

        _items.AddRange(requested.Select(snapshot => new OrderItem(snapshot)));
        RecalculateTotals();
    }

    private IReadOnlyList<OrderItemUpdate> ValidateItemUpdates(IEnumerable<OrderItemUpdate> updates)
    {
        var requested = updates.ToList();
        if (requested.Count == 0)
            throw new DomainException("Order must contain at least one item.");

        var existingIds = _items.Select(item => item.Id).ToHashSet();
        var requestedIds = new HashSet<Guid>();
        foreach (var update in requested)
        {
            OrderItem.Validate(update.Snapshot);
            if (!update.Id.HasValue)
                continue;
            if (update.Id.Value == Guid.Empty || !existingIds.Contains(update.Id.Value))
                throw new DomainException("Order item id is invalid for this order.");
            if (!requestedIds.Add(update.Id.Value))
                throw new DomainException("Order item ids must be unique.");
        }

        return requested;
    }

    private IReadOnlyList<string> SynchronizeItems(IReadOnlyList<OrderItemUpdate> updates)
    {
        var existingById = _items.ToDictionary(item => item.Id);
        var retainedIds = updates
            .Where(update => update.Id.HasValue)
            .Select(update => update.Id!.Value)
            .ToHashSet();
        var removedIds = existingById.Keys
            .Where(id => !retainedIds.Contains(id))
            .ToHashSet();
        var synchronized = new List<OrderItem>(updates.Count);

        foreach (var update in updates)
        {
            if (update.Id.HasValue)
            {
                var existing = existingById[update.Id.Value];
                existing.Update(update.Snapshot);
                synchronized.Add(existing);
            }
            else
            {
                synchronized.Add(new OrderItem(update.Snapshot));
            }
        }

        var removedImageUrls = _images
            .Where(image => image.OrderItemId.HasValue && removedIds.Contains(image.OrderItemId.Value))
            .Select(image => image.ImageUrl)
            .ToList();
        _images.RemoveAll(image => image.OrderItemId.HasValue && removedIds.Contains(image.OrderItemId.Value));

        _items.RemoveAll(item => removedIds.Contains(item.Id));
        for (var index = 0; index < synchronized.Count; index++)
        {
            var desired = synchronized[index];
            var currentIndex = _items.IndexOf(desired);
            if (currentIndex < 0)
            {
                _items.Insert(index, desired);
            }
            else if (currentIndex != index)
            {
                _items.RemoveAt(currentIndex);
                _items.Insert(index, desired);
            }
        }

        RecalculateTotals();
        return removedImageUrls;
    }

    private void RecalculateTotals()
    {
        SubTotal = Money(_items.Sum(item => item.UnitPrice * item.Quantity));
        DiscountTotal = Money(_items.Sum(item => item.DiscountAmount));
        TotalAmount = Money(SubTotal - DiscountTotal + ShippingFee);
        if (DepositAmount > TotalAmount)
            throw new DomainException("Deposit amount cannot exceed order total.");
    }

    private void AddChangeLog(
        string entityName,
        string fieldName,
        string? oldValue,
        string? newValue,
        string changeType,
        Guid? actorId,
        string? actorName,
        DateTime changedAt,
        string? note = null) =>
        _changeLogs.Add(new OrderChangeLog(
            entityName,
            fieldName,
            oldValue,
            newValue,
            changeType,
            actorId,
            actorName,
            changedAt,
            note));

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string EnumValue<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static string Required(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required.");
        if (value.Trim().Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength.ToString(CultureInfo.InvariantCulture)} characters.");
        return value.Trim();
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength.ToString(CultureInfo.InvariantCulture)} characters.");
        return normalized;
    }

    private static void EnsureUtc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException($"{field} must be UTC.");
    }
}
