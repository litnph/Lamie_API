using Lamie.Application.Orders;

namespace Lamie.API.Models.Orders;

public sealed class CreateOrderForm
{
    public string OrdererName { get; init; } = string.Empty;
    public string? OrdererPhone { get; init; }
    public Guid? ChannelId { get; init; }
    public string RecipientName { get; init; } = string.Empty;
    public string RecipientPhone { get; init; } = string.Empty;
    public bool PickupAtShop { get; init; }
    public bool ProvinceShipping { get; init; }
    public string? DeliveryAddress { get; init; }
    public string? DeliveryAddressDescription { get; init; }
    public decimal? DeliveryLatitude { get; init; }
    public decimal? DeliveryLongitude { get; init; }
    public DateTimeOffset DeliveryAt { get; init; }
    public DateTimeOffset? DeliveryTo { get; init; }
    public decimal DepositAmount { get; init; }
    public decimal ShippingFee { get; init; }
    public string? Description { get; init; }
    public string? ContentNote { get; init; }
    public List<OrderLineRequest> Items { get; init; } = [];
    public List<CreateOrderImageForm> Images { get; init; } = [];
}

public sealed class CreateOrderImageForm
{
    public IFormFile? ImageFile { get; init; }
    public int OrderItemIndex { get; init; }
    public int SortOrder { get; init; }
}

public sealed class UpdateOrderForm
{
    public Guid Id { get; init; }
    public string? RowVersion { get; init; }
    public string OrdererName { get; init; } = string.Empty;
    public string? OrdererPhone { get; init; }
    public Guid ChannelId { get; init; }
    public string RecipientName { get; init; } = string.Empty;
    public string RecipientPhone { get; init; } = string.Empty;
    public bool PickupAtShop { get; init; }
    public bool ProvinceShipping { get; init; }
    public string? DeliveryAddress { get; init; }
    public string? DeliveryAddressDescription { get; init; }
    public decimal? DeliveryLatitude { get; init; }
    public decimal? DeliveryLongitude { get; init; }
    public DateTimeOffset DeliveryAt { get; init; }
    public DateTimeOffset? DeliveryTo { get; init; }
    public decimal DepositAmount { get; init; }
    public decimal ShippingFee { get; init; }
    public decimal? ShippingFeeActual { get; init; }
    public string? Description { get; init; }
    public string? ContentNote { get; init; }
    public List<OrderLineRequest> Items { get; init; } = [];
    public List<CreateOrderImageForm> Images { get; init; } = [];
}
