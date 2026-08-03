using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed class OrderImage
{
    private OrderImage()
    {
    }

    internal OrderImage(Guid orderItemId, string imageUrl, int sortOrder, string? description)
    {
        if (orderItemId == Guid.Empty)
            throw new DomainException("Order item is required for an order image.");
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new DomainException("Order image URL is required.");
        if (imageUrl.Trim().Length > 2048)
            throw new DomainException("Order image URL cannot exceed 2048 characters.");
        if (sortOrder < 0)
            throw new DomainException("Order image sort order cannot be negative.");

        Id = Guid.NewGuid();
        OrderItemId = orderItemId;
        ImageUrl = imageUrl.Trim();
        SortOrder = sortOrder;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (Description?.Length > 1000)
            throw new DomainException("Order image description cannot exceed 1000 characters.");
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid? OrderItemId { get; private set; }
    public string ImageUrl { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public string? Description { get; private set; }

}
