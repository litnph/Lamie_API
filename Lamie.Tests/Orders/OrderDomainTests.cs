using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Lamie.Tests.Orders;

public sealed class OrderDomainTests
{
    private static readonly DateTime Baseline = new(2026, 7, 28, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NumericEnumsMatchFrontendContract()
    {
        Assert.Equal(1, (int)OrderStatus.Created);
        Assert.Equal(2, (int)OrderStatus.Producing);
        Assert.Equal(3, (int)OrderStatus.Shipping);
        Assert.Equal(4, (int)OrderStatus.Completed);
        Assert.Equal(99, (int)OrderStatus.Cancelled);
        Assert.Equal(1, (int)PaymentStatus.Unpaid);
        Assert.Equal(2, (int)PaymentStatus.Deposited);
        Assert.Equal(3, (int)PaymentStatus.Paid);
    }

    [Fact]
    public void OrderCalculatesServerTotalsAndSnapshotsLines()
    {
        var order = CreateOrder(
            deposit: 25,
            shipping: 15,
            items:
            [
                new OrderItemSnapshot(1, "ROSE-01", "Rose bouquet", "/rose.jpg", 100, 2, 10),
                new OrderItemSnapshot(null, null, "Custom card", null, 20, 1)
            ]);

        Assert.Equal(220, order.SubTotal);
        Assert.Equal(10, order.DiscountTotal);
        Assert.Equal(225, order.TotalAmount);
        Assert.Equal(PaymentStatus.Deposited, order.PaymentStatus);
        Assert.Equal(190, order.Items.First().LineTotal);
        Assert.Equal("Rose bouquet", order.Items.First().ProductName);
    }

    [Fact]
    public void SourceStateMachineAllowsOnlyAdjacentTransitionsAndTerminalStates()
    {
        var order = CreateOrder();

        Assert.True(order.RequiresInventoryReservation(OrderStatus.Producing));
        order.ChangeStatus(OrderStatus.Producing, true, Baseline.AddMinutes(1), null, "tester");
        Assert.True(order.InventoryReserved);
        Assert.Throws<DomainException>(() =>
            order.ChangeStatus(OrderStatus.Completed, true, Baseline.AddMinutes(2), null, "tester"));

        order.ChangeStatus(OrderStatus.Shipping, true, Baseline.AddMinutes(3), null, "tester");
        order.ChangeStatus(OrderStatus.Completed, true, Baseline.AddMinutes(4), null, "tester");

        Assert.Equal(Baseline.AddMinutes(4), order.CompletedAt);
        Assert.Throws<DomainException>(() =>
            order.ChangeStatus(OrderStatus.Cancelled, false, Baseline.AddMinutes(5), null, "tester"));
    }

    [Fact]
    public void CancellingReservedOrderMarksInventoryForRestore()
    {
        var order = CreateOrder();
        order.ChangeStatus(OrderStatus.Producing, true, Baseline.AddMinutes(1), null, "tester");

        Assert.True(order.RequiresInventoryRestore(OrderStatus.Cancelled));
        order.ChangeStatus(OrderStatus.Cancelled, false, Baseline.AddMinutes(2), null, "tester", "Customer request");

        Assert.False(order.InventoryReserved);
        Assert.Equal(Baseline.AddMinutes(2), order.CancelledAt);
        Assert.Contains(order.ChangeLogs, log => log.Note == "Customer request");
    }

    [Fact]
    public void PaymentStatusOnlyMovesForward()
    {
        var order = CreateOrder();

        order.ChangePaymentStatus(PaymentStatus.Deposited, Baseline.AddMinutes(1), null, "tester");
        order.ChangePaymentStatus(PaymentStatus.Paid, Baseline.AddMinutes(2), null, "tester");

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Throws<DomainException>(() =>
            order.ChangePaymentStatus(PaymentStatus.Unpaid, Baseline.AddMinutes(3), null, "tester"));
    }

    [Fact]
    public void ProductReservationCannotMakeStockNegativeAndCanBeRestored()
    {
        var product = new Product("ROSE-01", 100, 3, 1, 1);

        product.ReserveStock(2);
        Assert.Equal(1, product.Stock);
        Assert.Throws<DomainException>(() => product.ReserveStock(2));
        product.RestoreStock(2);
        Assert.Equal(3, product.Stock);
    }

    [Fact]
    public void MadeToOrderProductDoesNotReserveOrRestoreStock()
    {
        var product = new Product("BOUQUET-CUSTOM", 500, 12, 1, 1, tracksInventory: false);

        Assert.False(product.TracksInventory);
        Assert.Equal(0, product.Stock);

        product.ReserveStock(3);
        product.RestoreStock(3);

        Assert.Equal(0, product.Stock);
    }

    [Fact]
    public void MadeToOrderTransitionDoesNotClaimAnInventoryReservation()
    {
        var order = CreateOrder(items: [new OrderItemSnapshot(1, "CUSTOM-01", "Made to order bouquet", null, 500, 1)]);

        order.ChangeStatus(OrderStatus.Producing, false, Baseline.AddMinutes(1), null, "tester");

        Assert.Equal(OrderStatus.Producing, order.OrderStatus);
        Assert.False(order.InventoryReserved);
        Assert.False(order.RequiresInventoryRestore(OrderStatus.Cancelled));
    }

    [Fact]
    public void CreatedOrderUpdatesCustomerDeliveryItemsAndServerTotals()
    {
        var order = CreateOrder();
        var itemId = order.Items.Single().Id;
        var details = CreateDetails() with
        {
            OrdererName = "Updated orderer",
            OrdererPhone = "0987654321",
            RecipientName = "Updated recipient",
            RecipientPhone = "0909123456",
            DeliveryAddress = "456 Updated Street",
            DeliveryAddressDescription = "Blue gate",
            DeliveryAtUtc = Baseline.AddDays(2),
            DeliveryToUtc = Baseline.AddDays(2).AddHours(2),
            DepositAmount = 100,
            ShippingFee = 25,
            Description = "Updated delivery information",
            ContentNote = "Preserve this order note"
        };

        order.Update(
            Channel.AdminId,
            null,
            details,
            [new OrderItemUpdate(itemId, new OrderItemSnapshot(1, "ROSE-01", "Rose bouquet", null, 150, 2, Note: "Use white paper"))],
            Baseline.AddMinutes(1),
            null,
            "tester");

        Assert.Equal(OrderStatus.Created, order.OrderStatus);
        Assert.False(order.InventoryReserved);
        Assert.Equal("Updated orderer", order.OrdererName);
        Assert.Equal("Updated recipient", order.RecipientName);
        Assert.Equal("456 Updated Street", order.DeliveryAddress);
        Assert.Equal("Blue gate", order.DeliveryAddressDescription);
        Assert.Equal(details.DeliveryAtUtc, order.DeliveryAt);
        Assert.Equal(details.DeliveryToUtc, order.DeliveryTo);
        Assert.Equal("Preserve this order note", order.ContentNote);
        Assert.Equal("Use white paper", order.Items.Single().Note);
        Assert.Equal(300, order.SubTotal);
        Assert.Equal(325, order.TotalAmount);
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void TerminalOrderCannotBeEdited(OrderStatus terminalStatus)
    {
        var order = CreateOrder();
        if (terminalStatus == OrderStatus.Completed)
        {
            order.ChangeStatus(OrderStatus.Producing, true, Baseline.AddMinutes(1), null, "tester");
            order.ChangeStatus(OrderStatus.Shipping, true, Baseline.AddMinutes(2), null, "tester");
            order.ChangeStatus(OrderStatus.Completed, true, Baseline.AddMinutes(3), null, "tester");
        }
        else
        {
            order.ChangeStatus(OrderStatus.Cancelled, false, Baseline.AddMinutes(1), null, "tester");
        }

        var exception = Assert.Throws<DomainException>(() => order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [new OrderItemUpdate(order.Items.Single().Id, new OrderItemSnapshot(1, "ROSE-01", "Rose bouquet", null, 100, 1))],
            Baseline.AddMinutes(4),
            null,
            "tester"));

        Assert.Contains("đã hoàn tất hoặc đã hủy", exception.Message);
    }

    [Fact]
    public void InvalidUpdateQuantityIsRejectedBeforeExistingItemIsMutated()
    {
        var order = CreateOrder();
        var item = order.Items.Single();

        Assert.Throws<DomainException>(() => order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [new OrderItemUpdate(item.Id, new OrderItemSnapshot(1, "ROSE-01", "Changed name", null, 100, 0))],
            Baseline.AddMinutes(1),
            null,
            "tester"));

        Assert.Equal("Rose bouquet", item.ProductName);
        Assert.Equal(1, item.Quantity);
    }

    [Fact]
    public void ReservedOrderCannotBeEdited()
    {
        var order = CreateOrder();
        order.ChangeStatus(OrderStatus.Producing, true, Baseline.AddMinutes(1), null, "tester");

        Assert.Throws<DomainException>(() => order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [new OrderItemUpdate(order.Items.Single().Id, new OrderItemSnapshot(null, null, "Replacement", null, 10, 1))],
            Baseline.AddMinutes(2),
            null,
            "tester"));
    }

    [Fact]
    public void IllustrationStaysLinkedToItsOrderItemWhenDraftIsEdited()
    {
        var order = CreateOrder(items: [new OrderItemSnapshot(null, null, "Custom bouquet", null, 100, 1)]);
        var previousItemId = order.Items.Single().Id;
        order.AddImage(previousItemId, "/custom-bouquet.jpg", 0);

        var removedUrls = order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [new OrderItemUpdate(previousItemId, new OrderItemSnapshot(null, null, "Updated custom bouquet", null, 120, 1))],
            Baseline.AddMinutes(1),
            null,
            "tester");
        var currentItemId = order.Items.Single().Id;

        Assert.Empty(removedUrls);
        Assert.Equal(previousItemId, currentItemId);
        Assert.Equal(currentItemId, order.Images.Single().OrderItemId);
    }

    [Fact]
    public void ItemSynchronizationUpdatesInPlaceAndOnlyRemovesOmittedItems()
    {
        var order = CreateOrder(items:
        [
            new OrderItemSnapshot(1, "ROSE-01", "Rose bouquet", null, 100, 1),
            new OrderItemSnapshot(2, "TULIP-01", "Tulip bouquet", null, 50, 2)
        ]);
        var retained = order.Items.First();
        var removed = order.Items.Last();
        order.AddImage(retained.Id, "/rose.jpg", 0);
        order.AddImage(removed.Id, "/tulip.jpg", 0);

        var removedUrls = order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [
                new OrderItemUpdate(null, new OrderItemSnapshot(3, "LILY-01", "Lily bouquet", null, 25, 1)),
                new OrderItemUpdate(retained.Id, new OrderItemSnapshot(1, "ROSE-01", "Updated rose bouquet", null, 110, 2))
            ],
            Baseline.AddMinutes(1),
            null,
            "tester");

        Assert.Equal(2, order.Items.Count);
        Assert.Equal("LILY-01", order.Items.First().ProductSku);
        Assert.Same(retained, order.Items.Last());
        Assert.Equal("Updated rose bouquet", retained.ProductName);
        Assert.Equal(2, retained.Quantity);
        Assert.DoesNotContain(order.Items, item => item.Id == removed.Id);
        Assert.Equal(["/tulip.jpg"], removedUrls);
        Assert.Equal(retained.Id, order.Images.Single().OrderItemId);
        Assert.Equal("/rose.jpg", order.Images.Single().ImageUrl);
        Assert.Equal(245, order.SubTotal);
        Assert.Equal(255, order.TotalAmount);
    }

    [Fact]
    public void ItemSynchronizationRejectsDuplicateOrForeignIdsBeforeMutatingItems()
    {
        var order = CreateOrder();
        var existing = order.Items.Single();
        var originalName = existing.ProductName;

        Assert.Throws<DomainException>(() => order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [
                new OrderItemUpdate(existing.Id, new OrderItemSnapshot(1, "ROSE-01", "First mutation", null, 100, 1)),
                new OrderItemUpdate(existing.Id, new OrderItemSnapshot(1, "ROSE-01", "Duplicate mutation", null, 100, 1))
            ],
            Baseline.AddMinutes(1),
            null,
            "tester"));

        Assert.Throws<DomainException>(() => order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [new OrderItemUpdate(Guid.NewGuid(), new OrderItemSnapshot(1, "ROSE-01", "Foreign mutation", null, 100, 1))],
            Baseline.AddMinutes(1),
            null,
            "tester"));

        Assert.Single(order.Items);
        Assert.Same(existing, order.Items.Single());
        Assert.Equal(originalName, existing.ProductName);
    }

    [Fact]
    public void EfChangeTrackerUpdatesRetainedItemInsteadOfDeletingAndReinsertingIt()
    {
        var order = CreateOrder(items:
        [
            new OrderItemSnapshot(1, "ROSE-01", "Rose bouquet", null, 100, 1),
            new OrderItemSnapshot(2, "TULIP-01", "Tulip bouquet", null, 50, 1)
        ]);
        var retained = order.Items.First();
        var removed = order.Items.Last();
        order.AddImage(removed.Id, "/removed-tulip.jpg", 0);
        var removedImage = order.Images.Single();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieOrderTrackingOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        dbContext.Attach(order);
        var existingItemIds = order.Items.Select(item => item.Id).ToHashSet();
        var existingImageIds = order.Images.Select(image => image.Id).ToHashSet();
        var existingChangeLogIds = order.ChangeLogs.Select(log => log.Id).ToHashSet();

        order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [
                new OrderItemUpdate(retained.Id, new OrderItemSnapshot(1, "ROSE-01", "Updated rose bouquet", null, 120, 1)),
                new OrderItemUpdate(null, new OrderItemSnapshot(3, "LILY-01", "Lily bouquet", null, 80, 1))
            ],
            Baseline.AddMinutes(1),
            null,
            "tester");
        var added = order.Items.Single(item => !existingItemIds.Contains(item.Id));
        order.AddImage(added.Id, "/lily.jpg", 0);
        dbContext.OrderItems.AddRange(order.Items.Where(item => !existingItemIds.Contains(item.Id)));
        dbContext.OrderImages.AddRange(order.Images.Where(image => !existingImageIds.Contains(image.Id)));
        dbContext.OrderChangeLogs.AddRange(order.ChangeLogs.Where(log => !existingChangeLogIds.Contains(log.Id)));
        dbContext.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Modified, dbContext.Entry(retained).State);
        Assert.Equal(EntityState.Deleted, dbContext.Entry(removed).State);
        Assert.Equal(EntityState.Deleted, dbContext.Entry(removedImage).State);
        Assert.Equal(EntityState.Added, dbContext.Entry(added).State);
        Assert.Equal(EntityState.Added, dbContext.Entry(order.Images.Single()).State);
        Assert.Equal(order.Id, added.OrderId);
        Assert.Equal(order.Id, order.Images.Single().OrderId);
        Assert.All(
            order.ChangeLogs.Where(log => !existingChangeLogIds.Contains(log.Id)),
            log => Assert.Equal(EntityState.Added, dbContext.Entry(log).State));
        Assert.Single(dbContext.ChangeTracker.Entries<OrderItem>(), entry => entry.Entity.Id == retained.Id);
    }

    [Fact]
    public void DeliveryWindowMustBeUtcAndOptionalContactFieldsAreAccepted()
    {
        var nonUtc = CreateDetails() with { DeliveryAtUtc = DateTime.SpecifyKind(Baseline.AddDays(1), DateTimeKind.Unspecified) };
        Assert.Throws<DomainException>(() => new Order(
            "ORD-20260728-NONUTC",
            Channel.AdminId,
            null,
            nonUtc,
            [new OrderItemSnapshot(null, null, "Custom item", null, 10, 1)],
            Baseline,
            null,
            "tester"));

        var optionalContact = CreateDetails() with
        {
            OrdererPhone = string.Empty,
            DeliveryAddress = null,
            DeliveryToUtc = Baseline.AddDays(1).AddHours(2)
        };
        var optionalContactOrder = new Order(
            "ORD-20260728-NOADDR",
            Channel.AdminId,
            null,
            optionalContact,
            [new OrderItemSnapshot(null, null, "Custom item", null, 10, 1)],
            Baseline,
            null,
            "tester");

        Assert.Empty(optionalContactOrder.OrdererPhone);
        Assert.Null(optionalContactOrder.DeliveryAddress);
        Assert.Equal(optionalContact.DeliveryToUtc, optionalContactOrder.DeliveryTo);

        var reversedWindow = CreateDetails() with { DeliveryToUtc = Baseline };
        Assert.Throws<DomainException>(() => new Order(
            "ORD-20260728-WINDOW",
            Channel.AdminId,
            null,
            reversedWindow,
            [new OrderItemSnapshot(null, null, "Custom item", null, 10, 1)],
            Baseline,
            null,
            "tester"));

        var conflictingFulfillment = CreateDetails() with { PickupAtShop = true, ProvinceShipping = true };
        Assert.Throws<DomainException>(() => new Order(
            "ORD-20260728-FULFILLMENT",
            Channel.AdminId,
            null,
            conflictingFulfillment,
            [new OrderItemSnapshot(null, null, "Custom item", null, 10, 1)],
            Baseline,
            null,
            "tester"));

        var provinceOrder = new Order(
            "ORD-20260728-PROVINCE",
            Channel.AdminId,
            null,
            CreateDetails() with { ProvinceShipping = true },
            [new OrderItemSnapshot(null, null, "Custom item", null, 10, 1)],
            Baseline,
            null,
            "tester");
        Assert.True(provinceOrder.ProvinceShipping);

        var addressDescription = "Hẻm nhỏ, cổng màu xanh";
        var order = new Order(
            "ORD-20260728-ADDRESS-NOTE",
            Channel.AdminId,
            null,
            CreateDetails() with { DeliveryAddressDescription = addressDescription },
            [new OrderItemSnapshot(null, null, "Custom item", null, 10, 1)],
            Baseline,
            null,
            "tester");

        Assert.Equal(addressDescription, order.DeliveryAddressDescription);
    }

    [Fact]
    public void CustomerNormalizesPhoneAndEmailWithoutLosingDisplayValues()
    {
        var customer = new Customer("Nguyen Van A", "+84 912-345-678", " A@Example.Test ", null, Baseline);

        Assert.Equal("84912345678", customer.NormalizedPhone);
        Assert.Equal("a@example.test", customer.NormalizedEmail);
        Assert.Equal("+84 912-345-678", customer.Phone);
    }

    [Fact]
    public void EfModelUsesRestrictiveReferencesIndexesAndConcurrencyTokens()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieOrderModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var orderType = model.FindEntityType(typeof(Order));
        var itemType = model.FindEntityType(typeof(OrderItem));
        var productType = model.FindEntityType(typeof(Product));
        var imageType = model.FindEntityType(typeof(OrderImage));

        Assert.NotNull(orderType);
        Assert.Contains(orderType!.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(Order.OrderCode)]));
        Assert.All(orderType.GetForeignKeys(), foreignKey => Assert.NotEqual(DeleteBehavior.Cascade, foreignKey.DeleteBehavior));
        Assert.Equal(DeleteBehavior.Restrict,
            itemType!.GetForeignKeys().Single(foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(OrderItem.ProductId))).DeleteBehavior);
        Assert.True(orderType.FindProperty(nameof(Order.RowVersion))!.IsConcurrencyToken);
        Assert.True(productType!.FindProperty(nameof(Product.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(imageType!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(OrderImage.OrderItemId)]));
    }

    private static Order CreateOrder(
        decimal deposit = 0,
        decimal shipping = 10,
        IEnumerable<OrderItemSnapshot>? items = null) =>
        new(
            "ORD-20260728-ABC123",
            Channel.AdminId,
            null,
            CreateDetails() with { DepositAmount = deposit, ShippingFee = shipping },
            items ?? [new OrderItemSnapshot(1, "ROSE-01", "Rose bouquet", null, 100, 1)],
            Baseline,
            null,
            "tester");

    private static OrderDetails CreateDetails() => new(
        "Orderer",
        "0900000000",
        "Recipient",
        "0911111111",
        false,
        false,
        "123 Flower Street",
        null,
        10.7769m,
        106.7009m,
        Baseline.AddDays(1),
        null,
        0,
        10,
        null,
        null,
        null);
}
