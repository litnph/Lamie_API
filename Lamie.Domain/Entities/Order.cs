using System.Globalization;
using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public enum OrderStatus
{
    Created = 1,
    Producing = 2,
    Shipping = 3,
    Completed = 4,
    Confirmed = 5,
    Ready = 6,
    Delivered = 7,
    DeliveryFailed = 8,
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
    string? RecipientName,
    string? RecipientPhone,
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
    string? FullAddressSnapshot = null,
    string SenderName = "",
    string SenderPhone = "");

public sealed class Order
{
    private static readonly IReadOnlyDictionary<OrderStatus, IReadOnlySet<OrderStatus>> AllowedTransitions =
        new Dictionary<OrderStatus, IReadOnlySet<OrderStatus>>
        {
            [OrderStatus.Created] = new HashSet<OrderStatus> { OrderStatus.Confirmed, OrderStatus.Producing, OrderStatus.Cancelled },
            [OrderStatus.Confirmed] = new HashSet<OrderStatus> { OrderStatus.Producing, OrderStatus.Cancelled },
            [OrderStatus.Producing] = new HashSet<OrderStatus> { OrderStatus.Ready, OrderStatus.Cancelled },
            [OrderStatus.Ready] = new HashSet<OrderStatus> { OrderStatus.Shipping, OrderStatus.Delivered, OrderStatus.Cancelled },
            [OrderStatus.Shipping] = new HashSet<OrderStatus> { OrderStatus.Delivered, OrderStatus.DeliveryFailed },
            [OrderStatus.DeliveryFailed] = new HashSet<OrderStatus> { OrderStatus.Ready },
            [OrderStatus.Delivered] = new HashSet<OrderStatus>(),
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
        string? actorName,
        bool isPaid = false)
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
        // Payment state is advanced only by persisted receipt transactions. A deposit
        // amount on the order is a commercial term, not proof that cash was received.
        PaymentStatus = PaymentStatus.Unpaid;
        AddChangeLog("Order", "OrderStatus", null, EnumValue(OrderStatus), "Created", actorId, actorName, nowUtc);
    }

    public Guid Id { get; private set; }
    public string OrderCode { get; private set; } = string.Empty;
    public Guid? CustomerId { get; private set; }
    public Guid ChannelId { get; private set; }
    public string OrdererName { get; private set; } = string.Empty;
    public string OrdererPhone { get; private set; } = string.Empty;
    public string? RecipientName { get; private set; }
    public string? RecipientPhone { get; private set; }
    public string SenderName { get; private set; } = string.Empty;
    public string SenderPhone { get; private set; } = string.Empty;
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
    public DateTime? ShippingFeeIncurredAt { get; private set; }
    public DateTime? ShippingFeePaidAt { get; private set; }
    public PaymentMethod? ShippingFeePaymentMethod { get; private set; }
    public string? ShippingFeePaymentIdempotencyKey { get; private set; }
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
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? PaidInFullAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancelledReason { get; private set; }
    public DateOnly? PreparationDate { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public string? AssigneeName { get; private set; }
    public bool DesignConfirmed { get; private set; }
    public bool MaterialsPrepared { get; private set; }
    public bool CardPrepared { get; private set; }
    public bool QualityChecked { get; private set; }
    public string? HoldReason { get; private set; }
    public DateTime? HoldAt { get; private set; }
    public Guid? HoldById { get; private set; }
    public Guid? CreatedById { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public IReadOnlyCollection<OrderItem> Items => _items;
    public IReadOnlyCollection<OrderImage> Images => _images;
    public IReadOnlyCollection<OrderChangeLog> ChangeLogs => _changeLogs;

    public OrderStatus? PreviousStatus => OrderStatus switch
    {
        OrderStatus.Confirmed => OrderStatus.Created,
        OrderStatus.Producing => OrderStatus.Created,
        OrderStatus.Ready => OrderStatus.Producing,
        OrderStatus.Shipping => OrderStatus.Ready,
        OrderStatus.DeliveryFailed => OrderStatus.Shipping,
        _ => null
    };

    public bool RequiresInventoryReservation(OrderStatus target) =>
        !InventoryReserved && target is (OrderStatus.Producing or OrderStatus.Ready or OrderStatus.Shipping or OrderStatus.Delivered or OrderStatus.DeliveryFailed);

    public bool RequiresInventoryRestore(OrderStatus target) =>
        InventoryReserved && target is (OrderStatus.Created or OrderStatus.Confirmed or OrderStatus.Cancelled);

    public IReadOnlyList<string> Update(
        Guid channelId,
        Guid? customerId,
        OrderDetails details,
        IEnumerable<OrderItemUpdate> items,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName,
        bool? isPaid = null)
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
        ApplyDetails(details);
        var removedImageUrls = SynchronizeItems(requestedItems);
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
        if (HoldReason is not null)
            throw new DomainException("Đơn đang tạm dừng. Hãy tiếp tục xử lý trước khi chuyển bước.");
        if (target == OrderStatus.Shipping)
        {
            if (PickupAtShop)
                throw new DomainException("Đơn ghé lấy không có bước bàn giao shipper.");
            if (string.IsNullOrWhiteSpace(RecipientName) || string.IsNullOrWhiteSpace(RecipientPhone) || string.IsNullOrWhiteSpace(DeliveryAddress))
                throw new DomainException("Cần đủ tên, số điện thoại và địa chỉ người nhận trước khi bàn giao shipper.");
        }
        if (target == OrderStatus.Delivered && PickupAtShop != (OrderStatus == OrderStatus.Ready))
            throw new DomainException(PickupAtShop
                ? "Đơn ghé lấy phải được xác nhận nhận tại tiệm từ bước sẵn sàng."
                : "Đơn ship chỉ được xác nhận giao thành công từ bước đang giao.");
        if (target == OrderStatus.Cancelled && string.IsNullOrWhiteSpace(note))
            throw new DomainException("Lý do hủy đơn là bắt buộc.");

        var previous = OrderStatus;
        if (RequiresInventoryReservation(target))
            InventoryReserved = inventoryReservedAfterTransition;
        else if (RequiresInventoryRestore(target))
            InventoryReserved = false;

        OrderStatus = target;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        if (target == OrderStatus.Delivered)
        {
            DeliveredAt = nowUtc;
            if (ShippingFeeActual.HasValue && !ShippingFeeIncurredAt.HasValue)
                ShippingFeeIncurredAt = nowUtc;
            if (PaymentStatus == PaymentStatus.Paid)
            {
                OrderStatus = OrderStatus.Completed;
                CompletedAt = nowUtc;
            }
        }
        if (target == OrderStatus.Cancelled)
        {
            CancelledAt = nowUtc;
            CancelledReason = note!.Trim();
        }

        AddChangeLog("Order", "OrderStatus", EnumValue(previous), EnumValue(OrderStatus), "StatusChanged", actorId, actorName, nowUtc, note);
    }

    public void OverrideStatus(
        OrderStatus target,
        bool inventoryReservedAfterTransition,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName,
        string? note = null)
    {
        EnsureUtc(nowUtc, "Current time");
        if (!Enum.IsDefined(target)
            || target is OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Confirmed)
            throw new DomainException("Trạng thái đích không được phép chuyển thủ công.");
        if (OrderStatus is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new DomainException("Không thể chuyển trạng thái của đơn đã hoàn tất hoặc đã hủy.");
        if (target == OrderStatus.Shipping)
        {
            if (PickupAtShop)
                throw new DomainException("Đơn ghé lấy không có bước giao hàng.");
            if (string.IsNullOrWhiteSpace(RecipientName) || string.IsNullOrWhiteSpace(RecipientPhone) || string.IsNullOrWhiteSpace(DeliveryAddress))
                throw new DomainException("Cần đủ tên, số điện thoại và địa chỉ người nhận trước khi chuyển sang đang giao hàng.");
        }

        var previous = OrderStatus;
        if (RequiresInventoryReservation(target))
            InventoryReserved = inventoryReservedAfterTransition;
        else if (RequiresInventoryRestore(target))
            InventoryReserved = false;

        if (previous == OrderStatus.Delivered && target != OrderStatus.Delivered)
        {
            DeliveredAt = null;
            CompletedAt = null;
        }

        OrderStatus = target;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        if (target == OrderStatus.Delivered)
        {
            DeliveredAt = nowUtc;
            if (ShippingFeeActual.HasValue && !ShippingFeeIncurredAt.HasValue)
                ShippingFeeIncurredAt = nowUtc;
            if (PaymentStatus == PaymentStatus.Paid)
            {
                OrderStatus = OrderStatus.Completed;
                CompletedAt = nowUtc;
            }
        }

        AddChangeLog("Order", "OrderStatus", EnumValue(previous), EnumValue(OrderStatus), "StatusOverridden", actorId, actorName, nowUtc, note);
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
        if (target == PaymentStatus.Paid)
            PaidInFullAt = nowUtc;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "PaymentStatus", EnumValue(previous), EnumValue(target), "PaymentChanged", actorId, actorName, nowUtc, note);
        if (target == PaymentStatus.Paid && OrderStatus == OrderStatus.Delivered)
        {
            var priorOrderStatus = OrderStatus;
            OrderStatus = OrderStatus.Completed;
            CompletedAt = DeliveredAt.HasValue && PaidInFullAt.HasValue
                ? (DeliveredAt.Value >= PaidInFullAt.Value ? DeliveredAt.Value : PaidInFullAt.Value)
                : nowUtc;
            AddChangeLog("Order", "OrderStatus", EnumValue(priorOrderStatus), EnumValue(OrderStatus), "AutoCompleted", actorId, actorName, nowUtc, "Đã giao và đã thanh toán đủ.");
        }
    }

    public void ReconcilePaymentStatus(
        PaymentStatus target,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName,
        DateTime? paidInFullAtUtc = null,
        string? note = null)
    {
        EnsureUtc(nowUtc, "Current time");
        if (!Enum.IsDefined(target))
            throw new DomainException("Payment status is invalid.");
        if (target == PaymentStatus) return;
        var previous = PaymentStatus;
        PaymentStatus = target;
        if (paidInFullAtUtc.HasValue) EnsureUtc(paidInFullAtUtc.Value, "Paid in full time");
        PaidInFullAt = target == PaymentStatus.Paid ? paidInFullAtUtc ?? nowUtc : null;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "PaymentStatus", EnumValue(previous), EnumValue(target), "PaymentReconciled", actorId, actorName, nowUtc, note);
        if (target == PaymentStatus.Paid && OrderStatus == OrderStatus.Delivered)
        {
            var priorOrderStatus = OrderStatus;
            OrderStatus = OrderStatus.Completed;
            CompletedAt = DeliveredAt.HasValue && PaidInFullAt.HasValue
                ? (DeliveredAt.Value >= PaidInFullAt.Value ? DeliveredAt.Value : PaidInFullAt.Value)
                : nowUtc;
            AddChangeLog("Order", "OrderStatus", EnumValue(priorOrderStatus), EnumValue(OrderStatus), "AutoCompleted", actorId, actorName, nowUtc, "Đã giao và đã thanh toán đủ.");
        }
        else if (target != PaymentStatus.Paid && OrderStatus == OrderStatus.Completed)
        {
            var priorOrderStatus = OrderStatus;
            OrderStatus = OrderStatus.Delivered;
            CompletedAt = null;
            AddChangeLog("Order", "OrderStatus", EnumValue(priorOrderStatus), EnumValue(OrderStatus), "CompletionRecomputed", actorId, actorName, nowUtc, "Khoản thu được điều chỉnh nên đơn không còn đủ điều kiện hoàn tất.");
        }
    }

    public void ReturnToPreviousStatus(
        bool inventoryReservedAfterTransition,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName,
        string? note = null)
    {
        EnsureUtc(nowUtc, "Current time");
        var target = PreviousStatus
            ?? throw new DomainException($"Order status {OrderStatus} does not have a previous processing status.");
        var previous = OrderStatus;

        if (previous == OrderStatus.Producing)
            InventoryReserved = inventoryReservedAfterTransition;
        OrderStatus = target;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "OrderStatus", EnumValue(previous), EnumValue(target), "StatusReverted", actorId, actorName, nowUtc, note);
    }

    public void UpdateRecipient(string? name, string? phone, string? address, DateTime nowUtc, Guid? actorId, string? actorName)
    {
        EnsureMutable();
        EnsureUtc(nowUtc, "Current time");
        RecipientName = Optional(name, 200, "Recipient name");
        RecipientPhone = Optional(phone, 30, "Recipient phone");
        if (!PickupAtShop)
            DeliveryAddress = Optional(address, 1500, "Delivery address");
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "Recipient", null, RecipientName, "Updated", actorId, actorName, nowUtc);
    }

    public void UpdateAssignment(Guid? assigneeId, string? assigneeName, DateOnly? preparationDate, DateTime nowUtc, Guid? actorId, string? actorName)
    {
        EnsureMutable();
        EnsureUtc(nowUtc, "Current time");
        AssigneeId = assigneeId;
        AssigneeName = Optional(assigneeName, 200, "Assignee name");
        PreparationDate = preparationDate;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "Assignment", null, AssigneeName, "Updated", actorId, actorName, nowUtc, preparationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public void UpdateChecklist(bool? designConfirmed, bool? materialsPrepared, bool? cardPrepared, bool? qualityChecked, DateTime nowUtc, Guid? actorId, string? actorName)
    {
        EnsureMutable();
        EnsureUtc(nowUtc, "Current time");
        if (designConfirmed.HasValue) DesignConfirmed = designConfirmed.Value;
        if (materialsPrepared.HasValue) MaterialsPrepared = materialsPrepared.Value;
        if (cardPrepared.HasValue) CardPrepared = cardPrepared.Value;
        if (qualityChecked.HasValue) QualityChecked = qualityChecked.Value;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "Checklist", null, $"{DesignConfirmed},{MaterialsPrepared},{CardPrepared},{QualityChecked}", "Updated", actorId, actorName, nowUtc);
    }

    public void Hold(string reason, DateTime nowUtc, Guid? actorId, string? actorName)
    {
        EnsureMutable();
        EnsureUtc(nowUtc, "Current time");
        if (HoldReason is not null) return;
        HoldReason = Required(reason, 1000, "Hold reason");
        HoldAt = nowUtc;
        HoldById = actorId;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "HoldReason", null, HoldReason, "Held", actorId, actorName, nowUtc);
    }

    public void Resume(DateTime nowUtc, Guid? actorId, string? actorName)
    {
        EnsureMutable();
        EnsureUtc(nowUtc, "Current time");
        if (HoldReason is null) return;
        var previous = HoldReason;
        HoldReason = null;
        HoldAt = null;
        HoldById = null;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog("Order", "HoldReason", previous, null, "Resumed", actorId, actorName, nowUtc);
    }

    public void UpdateShippingFeeActual(
        decimal? shippingFeeActual,
        DateTime nowUtc,
        Guid? actorId,
        string? actorName,
        DateTime? incurredAtUtc = null,
        DateTime? paidAtUtc = null,
        PaymentMethod? paymentMethod = null,
        string? paymentIdempotencyKey = null)
    {
        EnsureUtc(nowUtc, "Current time");
        if (OrderStatus == OrderStatus.Cancelled)
            throw new DomainException("Không thể ghi phí giao hàng thực tế cho đơn đã hủy.");
        if (shippingFeeActual < 0)
            throw new DomainException("Phí giao hàng thực tế không thể là số âm.");
        if (PickupAtShop && shippingFeeActual.HasValue)
            throw new DomainException("Đơn nhận tại cửa hàng không có phí giao hàng thực tế.");
        if (incurredAtUtc.HasValue) EnsureUtc(incurredAtUtc.Value, "Shipping expense incurred time");
        if (paidAtUtc.HasValue) EnsureUtc(paidAtUtc.Value, "Shipping expense paid time");
        if (paidAtUtc.HasValue != paymentMethod.HasValue)
            throw new DomainException("Ngày trả phí ship và phương thức thanh toán phải được nhập cùng nhau.");
        if (paymentMethod.HasValue && !Enum.IsDefined(paymentMethod.Value))
            throw new DomainException("Phương thức trả phí ship không hợp lệ.");
        if (!string.IsNullOrWhiteSpace(paymentIdempotencyKey) && !paidAtUtc.HasValue)
            throw new DomainException("Mã chống trùng phí ship yêu cầu ngày thực trả.");

        decimal? normalized = shippingFeeActual.HasValue ? Money(shippingFeeActual.Value) : null;
        var normalizedPaymentKey = normalized.HasValue
            ? Optional(paymentIdempotencyKey, 100, "Shipping payment idempotency key")
                ?? (paidAtUtc.HasValue ? ShippingFeePaymentIdempotencyKey : null)
            : null;
        if (ShippingFeeActual == normalized
            && ShippingFeeIncurredAt == (normalized.HasValue ? incurredAtUtc ?? ShippingFeeIncurredAt : null)
            && ShippingFeePaidAt == (normalized.HasValue ? paidAtUtc : null)
            && ShippingFeePaymentMethod == (normalized.HasValue ? paymentMethod : null)
            && ShippingFeePaymentIdempotencyKey == normalizedPaymentKey)
            return;

        var previous = ShippingFeeActual;
        ShippingFeeActual = normalized;
        ShippingFeeIncurredAt = normalized.HasValue ? incurredAtUtc ?? ShippingFeeIncurredAt : null;
        ShippingFeePaidAt = normalized.HasValue ? paidAtUtc : null;
        ShippingFeePaymentMethod = normalized.HasValue ? paymentMethod : null;
        ShippingFeePaymentIdempotencyKey = normalizedPaymentKey;
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
        AddChangeLog(
            "Order",
            "ShippingFeeActual",
            previous?.ToString(CultureInfo.InvariantCulture),
            normalized?.ToString(CultureInfo.InvariantCulture),
            "Updated",
            actorId,
            actorName,
            nowUtc);
    }

    public void TouchMaterialConfiguration(DateTime nowUtc, Guid? actorId)
    {
        EnsureUtc(nowUtc, "Current time");
        if (OrderStatus is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new DomainException("Materials cannot be changed after an order is completed or cancelled.");
        UpdatedAt = nowUtc;
        UpdatedById = actorId;
    }

    private void EnsureMutable()
    {
        if (OrderStatus is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new DomainException("Không thể thay đổi đơn đã hoàn tất hoặc đã hủy.");
    }

    private void ApplyDetails(OrderDetails details)
    {
        OrdererName = Required(details.OrdererName, 200, "Orderer name");
        OrdererPhone = Required(details.OrdererPhone, 30, "Orderer phone");
        RecipientName = Optional(details.RecipientName, 200, "Recipient name");
        RecipientPhone = Optional(details.RecipientPhone, 30, "Recipient phone");
        SenderName = Optional(details.SenderName, 200, "Sender name") ?? string.Empty;
        SenderPhone = Optional(details.SenderPhone, 30, "Sender phone") ?? string.Empty;
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
