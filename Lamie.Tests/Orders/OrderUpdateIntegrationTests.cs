using Lamie.API.Models.Orders;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Inventory;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lamie.Tests.Orders;

public sealed class OrderUpdateIntegrationTests
{
    [Fact]
    public async Task CreateOrderPersistsActualShippingFee()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("SHIPPING-ACTUAL", 0, false, 100_000));
        var product = environment.Products[0];

        var created = await environment.Service.CreateAsync(new CreateOrderForm
        {
            OrdererName = "Khach dat",
            OrdererPhone = "0900000001",
            CreateMember = false,
            PickupAtShop = false,
            DeliveryAddress = "461 Phan Van Tri",
            DeliveryAt = TestEnvironment.Now.AddDays(1),
            ShippingFee = 50_000,
            ShippingFeeActual = 42_000,
            Items =
            [
                new OrderLineRequest
                {
                    ProductId = product.Id.ToString(),
                    ProductName = "Hoa",
                    UnitPrice = 100_000,
                    Quantity = 1
                }
            ]
        }, CancellationToken.None);

        Assert.Equal(42_000, created.ShippingFeeActual);
        environment.DbContext.ChangeTracker.Clear();
        Assert.Equal(42_000, (await environment.DbContext.Orders.SingleAsync()).ShippingFeeActual);
    }

    [Fact]
    public async Task CreateOutsideCatalogItemPersistsSelectedCategoryAndProductType()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var categoryId = await environment.DbContext.Categories.Select(category => category.Id).SingleAsync();
        var productTypeId = await environment.DbContext.ProductTypes.Select(productType => productType.Id).SingleAsync();

        var created = await environment.Service.CreateAsync(new CreateOrderForm
        {
            OrdererName = "Khach dat",
            OrdererPhone = "0900000001",
            CreateMember = false,
            PickupAtShop = true,
            DeliveryAt = TestEnvironment.Now.AddDays(1),
            Items =
            [
                new OrderLineRequest
                {
                    CategoryId = categoryId,
                    ProductTypeId = productTypeId,
                    ProductName = "Sản phẩm ngoài danh mục",
                    UnitPrice = 350_000,
                    Quantity = 1
                }
            ]
        }, CancellationToken.None);

        var item = Assert.Single(created.Items);
        Assert.Equal(categoryId, item.CategoryId);
        Assert.Equal(productTypeId, item.ProductTypeId);
    }

    [Fact]
    public async Task CreateOrderUsesExplicitMemberIntentAndKeepsRecipientOptional()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("MEMBER-FLOW", 0, false, 100_000));
        var product = environment.Products[0];
        var existing = new Customer("Member gốc", "0901234567", null, null, TestEnvironment.Now.UtcDateTime);
        environment.DbContext.Customers.Add(existing);
        await environment.DbContext.SaveChangesAsync();

        CreateOrderForm Form(string name, string phone, bool createMember, Guid? customerId = null) => new()
        {
            OrdererName = name,
            OrdererPhone = phone,
            CustomerId = customerId,
            CreateMember = createMember,
            SenderName = "Người gửi",
            PickupAtShop = true,
            DeliveryAt = TestEnvironment.Now.AddDays(1),
            Items = [new OrderLineRequest { ProductId = product.Id.ToString(), ProductName = "Hoa", UnitPrice = 100_000, Quantity = 1 }]
        };

        var withoutMember = await environment.Service.CreateAsync(Form("Khách lẻ", "0900000001", false), CancellationToken.None);
        var nameOnly = await environment.Service.CreateAsync(Form("Khách mới", "0900000002", true), CancellationToken.None);
        var selected = await environment.Service.CreateAsync(Form("Tên chỉ dùng trên đơn", "0999999999", false, existing.Id), CancellationToken.None);

        Assert.Null(withoutMember.CustomerId);
        Assert.Null(withoutMember.RecipientName);
        Assert.Null(withoutMember.RecipientPhone);
        Assert.Equal("Người gửi", withoutMember.SenderName);
        Assert.NotNull(nameOnly.CustomerId);
        Assert.Equal(existing.Id, selected.CustomerId);
        environment.DbContext.ChangeTracker.Clear();
        var persistedExisting = await environment.DbContext.Customers.SingleAsync(customer => customer.Id == existing.Id);
        Assert.Equal("Member gốc", persistedExisting.Name);
        Assert.Equal("0901234567", persistedExisting.Phone);

        await Assert.ThrowsAsync<ConflictException>(() =>
            environment.Service.CreateAsync(Form("Tên khác", "0901234567", true), CancellationToken.None));
    }

    [Fact]
    public async Task SameProductLinesKeepIndependentIngredientSnapshotsWhenProductRecipeChanges()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-SNAPSHOT", 0, false, 100_000));
        var unit = new MeasurementUnit("STEM", "Cành", "cành", false, true, TestEnvironment.Now.UtcDateTime);
        environment.DbContext.MeasurementUnits.Add(unit);
        await environment.DbContext.SaveChangesAsync();
        var ingredient = new Ingredient("ROSE_RED", "Hoa hồng đỏ", unit.Id, null, true, TestEnvironment.Now.UtcDateTime);
        environment.DbContext.Ingredients.Add(ingredient);
        await environment.DbContext.SaveChangesAsync();
        var product = await environment.DbContext.Products.SingleAsync();
        product.ReplaceIngredients([new ProductIngredientDefinition(ingredient.Id, 10, null, 0)]);
        await environment.DbContext.SaveChangesAsync();
        environment.DbContext.ChangeTracker.Clear();

        var created = await environment.Service.CreateAsync(new CreateOrderForm
        {
            OrdererName = "Orderer",
            OrdererPhone = "0900000001",
            RecipientName = "Recipient",
            RecipientPhone = "0900000000",
            PickupAtShop = true,
            DeliveryAt = TestEnvironment.Now.AddDays(1),
            Items =
            [
                new OrderLineRequest { ProductId = product.Id.ToString(), ProductName = "Rose", UnitPrice = 100_000, Quantity = 1 },
                new OrderLineRequest { ProductId = product.Id.ToString(), ProductName = "Rose", UnitPrice = 100_000, Quantity = 1 }
            ]
        }, CancellationToken.None);
        Assert.All(created.Items, item => Assert.Equal(10, Assert.Single(item.IngredientSnapshots).PerProductBaseQuantity));
        Assert.NotEqual(created.Items[0].IngredientSnapshots.Single().Id, created.Items[1].IngredientSnapshots.Single().Id);

        product = await environment.DbContext.Products.Include(item => item.Ingredients).SingleAsync();
        product.ReplaceIngredients([new ProductIngredientDefinition(ingredient.Id, 12, null, 0)]);
        await environment.DbContext.SaveChangesAsync();
        environment.DbContext.ChangeTracker.Clear();
        var update = CreateUpdateForm(created, product, 1, overrides: new UpdateOverrides
        {
            Items =
            [
                new OrderLineRequest
                {
                    Id = created.Items[0].Id.ToString(), ProductId = product.Id.ToString(), ProductName = "Rose",
                    UnitPrice = 100_000, Quantity = 2
                },
                new OrderLineRequest
                {
                    Id = created.Items[1].Id.ToString(), ProductId = product.Id.ToString(), ProductName = "Rose",
                    UnitPrice = 100_000, Quantity = 1, IngredientsSpecified = true,
                    Ingredients = [new OrderLineIngredientRequest { IngredientId = ingredient.Id, BaseQuantity = 7, SortOrder = 0 }]
                }
            ]
        });

        var updated = await environment.Service.UpdateAsync(created.Id, update, CancellationToken.None);

        Assert.Equal(10, updated.Items[0].IngredientSnapshots.Single().PerProductBaseQuantity);
        Assert.Equal(20, updated.Items[0].IngredientSnapshots.Single().TotalBaseQuantity);
        Assert.Equal(7, updated.Items[1].IngredientSnapshots.Single().PerProductBaseQuantity);
        Assert.Equal(7, updated.Items[1].IngredientSnapshots.Single().TotalBaseQuantity);

        var cleared = await environment.Service.UpdateAsync(created.Id, CreateUpdateForm(updated, product, 1, overrides: new UpdateOverrides
        {
            Items =
            [
                new OrderLineRequest
                {
                    Id = updated.Items[0].Id.ToString(), ProductId = product.Id.ToString(), ProductName = "Rose",
                    UnitPrice = 100_000, Quantity = 2, IngredientsSpecified = true, Ingredients = []
                },
                new OrderLineRequest
                {
                    Id = updated.Items[1].Id.ToString(), ProductId = product.Id.ToString(), ProductName = "Rose",
                    UnitPrice = 100_000, Quantity = 1
                }
            ]
        }), CancellationToken.None);

        Assert.Empty(cleared.Items[0].IngredientSnapshots);
        Assert.Equal(7, cleared.Items[1].IngredientSnapshots.Single().PerProductBaseQuantity);
    }

    [Fact]
    public async Task CreatedOrderUpdatePersistsCustomerDeliveryItemNoteImageAndServerTotals()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-UPDATE", 10, true, 100));
        var order = await environment.AddOrderAsync(
            [new OrderLineSeed(environment.Products[0], 1, "Original note")],
            illustrationUrl: "https://test.local/orders/original.jpg");
        var before = await environment.Service.GetAsync(order.Id, CancellationToken.None);
        var form = CreateUpdateForm(before, environment.Products[0], quantity: 2, unitPrice: 125, new UpdateOverrides
        {
            OrdererName = "Updated orderer",
            OrdererPhone = "0987654321",
            RecipientName = "Updated recipient",
            RecipientPhone = "0909123456",
            DeliveryAddress = "456 Updated Street",
            DeliveryAddressDescription = "Blue gate",
            DeliveryAt = TestEnvironment.Now.AddDays(2),
            DeliveryTo = TestEnvironment.Now.AddDays(2).AddHours(2),
            DepositAmount = 100,
            ShippingFee = 25,
            Description = "Updated delivery information",
            ContentNote = "Preserve this order note",
            Items =
            [
                CreateLine(before.Items.Single(), environment.Products[0], quantity: 2, unitPrice: 125, note: "Use white paper")
            ]
        });

        var updated = await environment.Service.UpdateAsync(order.Id, form, CancellationToken.None);

        Assert.Equal(OrderStatus.Created, updated.OrderStatus);
        Assert.Equal("Updated orderer", updated.OrdererName);
        Assert.Equal("Updated recipient", updated.RecipientName);
        Assert.Equal("456 Updated Street", updated.DeliveryAddress);
        Assert.Equal("Blue gate", updated.DeliveryAddressDescription);
        Assert.Equal("Preserve this order note", updated.ContentNote);
        Assert.Equal("Use white paper", updated.Items.Single().Note);
        Assert.Equal(250, updated.SubTotal);
        Assert.Equal(275, updated.TotalAmount);
        Assert.Single(updated.Images);
        Assert.Equal("https://test.local/orders/original.jpg", updated.Images.Single().ImageUrl);
        Assert.False(string.IsNullOrWhiteSpace(updated.RowVersion));
    }

    [Fact]
    public async Task CreatedOrderCanBeUpdatedWithoutRecipientPhone()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-NO-PHONE", 10, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        var before = await environment.Service.GetAsync(order.Id, CancellationToken.None);

        var updated = await environment.Service.UpdateAsync(
            order.Id,
            CreateUpdateForm(before, environment.Products[0], quantity: 1, overrides: new UpdateOverrides
            {
                ClearRecipientPhone = true
            }),
            CancellationToken.None);

        Assert.Null(updated.RecipientPhone);
        Assert.Equal(before.OrdererPhone, updated.OrdererPhone);
    }

    [Fact]
    public async Task CreatedOrderUpdatePersistsInventoryMaterialsThroughExistingInventoryService()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-MATERIAL", 10, true, 100));
        var measurementUnit = new MeasurementUnit("STEM", "Cành", "cành", true, true, TestEnvironment.Now.UtcDateTime);
        environment.DbContext.MeasurementUnits.Add(measurementUnit);
        await environment.DbContext.SaveChangesAsync();
        var inventoryItem = new InventoryItem("ROSE_STEM", "Cành hồng", measurementUnit.Id, null, true, TestEnvironment.Now.UtcDateTime);
        var stockUnit = new InventoryStockUnit(inventoryItem.Id, null, 5, true, TestEnvironment.Now.UtcDateTime);
        environment.DbContext.AddRange(inventoryItem, stockUnit);
        await environment.DbContext.SaveChangesAsync();

        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        var before = await environment.Service.GetAsync(order.Id, CancellationToken.None);
        var updated = await environment.Service.UpdateAsync(
            order.Id,
            CreateUpdateForm(before, environment.Products[0], quantity: 1, overrides: new UpdateOverrides
            {
                Materials = [new SaveOrderMaterialItem(null, stockUnit.Id, 2.5m)]
            }),
            CancellationToken.None);

        var material = Assert.Single(updated.Materials!);
        Assert.Equal(stockUnit.Id, material.StockUnitId);
        Assert.Equal(2.5m, material.RequiredQuantity);
        Assert.Equal(0, material.DeductedQuantity);
        Assert.Single(await environment.DbContext.OrderMaterials.Where(item => item.OrderId == order.Id).ToListAsync());
    }

    [Fact]
    public async Task EditedQuantityIsReservedWhenCreatedOrderMovesToProducing()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-RESERVE", 5, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        var before = await environment.Service.GetAsync(order.Id, CancellationToken.None);

        await environment.Service.UpdateAsync(
            order.Id,
            CreateUpdateForm(before, environment.Products[0], quantity: 3),
            CancellationToken.None);
        await environment.Service.RunWorkflowActionAsync(order.Id, "confirm", new(), CancellationToken.None);
        await environment.Service.RunWorkflowActionAsync(order.Id, "start-preparation", new(), CancellationToken.None);
        environment.DbContext.ChangeTracker.Clear();

        var persistedProduct = await environment.DbContext.Products.SingleAsync();
        var persistedOrder = await environment.DbContext.Orders.SingleAsync();
        Assert.Equal(2, persistedProduct.Stock);
        Assert.Equal(OrderStatus.Producing, persistedOrder.OrderStatus);
        Assert.True(persistedOrder.InventoryReserved);
    }

    [Fact]
    public async Task PrepareOrderMovesCreatedOrderDirectlyToProducing()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-DIRECT-PREPARE", 5, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 2)]);

        var updated = await environment.Service.RunWorkflowActionAsync(
            order.Id,
            "prepare-order",
            new(),
            CancellationToken.None);

        Assert.Equal(OrderStatus.Producing, updated.OrderStatus);
        Assert.Equal(3, (await environment.DbContext.Products.SingleAsync()).Stock);
    }

    [Fact]
    public async Task PrivilegedOverridePersistsAuditAndStillRejectsManualCompletion()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-OVERRIDE", 5, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        var before = await environment.Service.GetAsync(order.Id, CancellationToken.None);

        var updated = await environment.Service.OverrideStatusAsync(
            order.Id,
            new OverrideOrderStatusRequest(OrderStatus.Ready, before.RowVersion, "Khớp tình trạng thực tế"),
            CancellationToken.None);

        Assert.Equal(OrderStatus.Ready, updated.OrderStatus);
        Assert.Contains(updated.ChangeLogs, log => log.ChangeType == "StatusOverridden");
        await Assert.ThrowsAsync<DomainException>(() => environment.Service.OverrideStatusAsync(
            order.Id,
            new OverrideOrderStatusRequest(OrderStatus.Completed, updated.RowVersion),
            CancellationToken.None));
    }

    [Fact]
    public async Task InsufficientStockRollsBackEveryInventoryAndOrderChange()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-ENOUGH", 5, true, 100),
            new ProductSeed("TULIP-SHORT", 1, true, 80));
        var order = await environment.AddOrderAsync(
        [
            new OrderLineSeed(environment.Products[0], 4),
            new OrderLineSeed(environment.Products[1], 2)
        ]);

        await environment.Service.RunWorkflowActionAsync(order.Id, "confirm", new(), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            environment.Service.RunWorkflowActionAsync(order.Id, "start-preparation", new(), CancellationToken.None));
        environment.DbContext.ChangeTracker.Clear();

        var products = await environment.DbContext.Products.OrderBy(product => product.Id).ToListAsync();
        var persistedOrder = await environment.DbContext.Orders.SingleAsync();
        Assert.Contains("không đủ tồn kho", exception.Message);
        Assert.Equal([5, 1], products.Select(product => product.Stock));
        Assert.Equal(OrderStatus.Confirmed, persistedOrder.OrderStatus);
        Assert.False(persistedOrder.InventoryReserved);
    }

    [Fact]
    public async Task CancellingProducingOrderRestoresReservedInventory()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-CANCEL", 5, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 3)]);

        await environment.Service.RunWorkflowActionAsync(order.Id, "confirm", new(), CancellationToken.None);
        await environment.Service.RunWorkflowActionAsync(order.Id, "start-preparation", new(), CancellationToken.None);
        await environment.Service.RunWorkflowActionAsync(order.Id, "cancel", new(Reason: "Khách hủy"), CancellationToken.None);
        environment.DbContext.ChangeTracker.Clear();

        Assert.Equal(5, (await environment.DbContext.Products.SingleAsync()).Stock);
        var persistedOrder = await environment.DbContext.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, persistedOrder.OrderStatus);
        Assert.False(persistedOrder.InventoryReserved);
    }

    [Fact]
    public async Task ReturningProducingOrderToCreatedRestoresProductAndMaterialInventory()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-REVERSE", 5, true, 100));
        var measurementUnit = new MeasurementUnit("STEM-REVERSE", "Cành", "cành", true, true, TestEnvironment.Now.UtcDateTime);
        environment.DbContext.MeasurementUnits.Add(measurementUnit);
        await environment.DbContext.SaveChangesAsync();
        var inventoryItem = new InventoryItem("ROSE_STEM_REVERSE", "Cành hồng", measurementUnit.Id, null, true, TestEnvironment.Now.UtcDateTime);
        var stockUnit = new InventoryStockUnit(inventoryItem.Id, null, 10, true, TestEnvironment.Now.UtcDateTime);
        stockUnit.Add(10, TestEnvironment.Now.UtcDateTime);
        environment.DbContext.AddRange(inventoryItem, stockUnit);
        await environment.DbContext.SaveChangesAsync();

        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 3)]);
        var before = await environment.Service.GetAsync(order.Id, CancellationToken.None);
        await environment.Service.UpdateAsync(
            order.Id,
            CreateUpdateForm(before, environment.Products[0], quantity: 3, overrides: new UpdateOverrides
            {
                Materials = [new SaveOrderMaterialItem(null, stockUnit.Id, 2.5m)]
            }),
            CancellationToken.None);
        await environment.Service.RunWorkflowActionAsync(order.Id, "confirm", new(), CancellationToken.None);
        await environment.Service.RunWorkflowActionAsync(order.Id, "start-preparation", new(), CancellationToken.None);
        var producing = await environment.Service.GetAsync(order.Id, CancellationToken.None);
        await environment.Service.ReturnToPreviousStatusAsync(
            order.Id,
            new ReturnOrderToPreviousStatusRequest(producing.RowVersion),
            CancellationToken.None);
        environment.DbContext.ChangeTracker.Clear();

        Assert.Equal(5, (await environment.DbContext.Products.SingleAsync()).Stock);
        Assert.Equal(10, (await environment.DbContext.InventoryStockUnits.SingleAsync()).Quantity);
        Assert.Equal(0, (await environment.DbContext.OrderMaterials.SingleAsync()).DeductedQuantity);
        var persistedOrder = await environment.DbContext.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Created, persistedOrder.OrderStatus);
        Assert.False(persistedOrder.InventoryReserved);
    }

    [Fact]
    public async Task MadeToOrderProductDoesNotCreateAFalseReservation()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("CUSTOM-BOUQUET", 0, false, 500));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 2)]);

        await environment.Service.RunWorkflowActionAsync(order.Id, "confirm", new(), CancellationToken.None);
        await environment.Service.RunWorkflowActionAsync(order.Id, "start-preparation", new(), CancellationToken.None);
        environment.DbContext.ChangeTracker.Clear();

        Assert.Equal(0, (await environment.DbContext.Products.SingleAsync()).Stock);
        var persistedOrder = await environment.DbContext.Orders.SingleAsync();
        Assert.Equal(OrderStatus.Producing, persistedOrder.OrderStatus);
        Assert.False(persistedOrder.InventoryReserved);
    }

    [Fact]
    public async Task InvalidProductIsRejectedWithoutMutatingCreatedOrder()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-VALID", 5, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        var before = await environment.Service.GetAsync(order.Id, CancellationToken.None);
        var form = CreateUpdateForm(before, environment.Products[0], quantity: 1, overrides: new UpdateOverrides
        {
            Items =
            [
                new OrderLineRequest
                {
                    Id = before.Items.Single().Id.ToString(),
                    ProductId = "999999",
                    ProductName = "Unknown product",
                    UnitPrice = 100,
                    Quantity = 1
                }
            ]
        });

        await Assert.ThrowsAsync<NotFoundException>(() =>
            environment.Service.UpdateAsync(order.Id, form, CancellationToken.None));
        environment.DbContext.ChangeTracker.Clear();

        var persistedItem = await environment.DbContext.OrderItems.SingleAsync();
        Assert.Equal(environment.Products[0].Id, persistedItem.ProductId);
        Assert.Equal(1, persistedItem.Quantity);
    }

    [Fact]
    public async Task InvalidQuantityIsRejectedWithoutMutatingCreatedOrder()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-QUANTITY", 5, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        var before = await environment.Service.GetAsync(order.Id, CancellationToken.None);
        var form = CreateUpdateForm(before, environment.Products[0], quantity: 1, overrides: new UpdateOverrides
        {
            Items = [CreateLine(before.Items.Single(), environment.Products[0], quantity: 0, unitPrice: 100)]
        });

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            environment.Service.UpdateAsync(order.Id, form, CancellationToken.None));
        environment.DbContext.ChangeTracker.Clear();

        Assert.Contains("Số lượng", exception.Errors["items"].Single());
        Assert.Equal(1, (await environment.DbContext.OrderItems.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task StaleRowVersionReturnsConflictWithoutOverwritingConcurrentChange()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-CONCURRENCY", 5, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        var stale = await environment.Service.GetAsync(order.Id, CancellationToken.None);

        await environment.Service.AddPaymentAsync(
            order.Id,
            new CreateOrderPaymentRequest(
                10,
                OrderPaymentKind.Deposit,
                PaymentMethod.BankTransfer,
                TestEnvironment.Now,
                "stale-row-version-payment"),
            CancellationToken.None);
        environment.DbContext.ChangeTracker.Clear();

        var staleForm = CreateUpdateForm(stale, environment.Products[0], quantity: 2, overrides: new UpdateOverrides
        {
            RecipientName = "Stale overwrite"
        });
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            environment.Service.UpdateAsync(order.Id, staleForm, CancellationToken.None));
        environment.DbContext.ChangeTracker.Clear();

        var persisted = await environment.DbContext.Orders.SingleAsync();
        Assert.Contains("vui lòng tải lại", exception.Message);
        Assert.Equal(PaymentStatus.Deposited, persisted.PaymentStatus);
        Assert.NotEqual("Stale overwrite", persisted.RecipientName);
    }

    [Fact]
    public async Task ReservedProducingOrderCannotBeEdited()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("ROSE-LOCKED", 5, true, 100));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        await environment.Service.RunWorkflowActionAsync(order.Id, "confirm", new(), CancellationToken.None);
        await environment.Service.RunWorkflowActionAsync(order.Id, "start-preparation", new(), CancellationToken.None);
        environment.DbContext.ChangeTracker.Clear();
        var producing = await environment.Service.GetAsync(order.Id, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            environment.Service.UpdateAsync(
                order.Id,
                CreateUpdateForm(producing, environment.Products[0], quantity: 2),
                CancellationToken.None));

        Assert.Contains("trạng thái Đã tạo", exception.Message);
    }

    [Fact]
    public async Task PaymentIdempotencyReturnsTheSameTransactionAndRejectsDifferentContent()
    {
        await using var environment = await TestEnvironment.CreateAsync(
            new ProductSeed("PAYMENT-IDEMPOTENCY", 0, false, 1_100_000));
        var order = await environment.AddOrderAsync([new OrderLineSeed(environment.Products[0], 1)]);
        var request = new CreateOrderPaymentRequest(
            600_000,
            OrderPaymentKind.Deposit,
            PaymentMethod.BankTransfer,
            new DateTimeOffset(2026, 10, 9, 3, 0, 0, TimeSpan.Zero),
            "payment-idempotency-test");

        var first = await environment.Service.AddPaymentAsync(order.Id, request, CancellationToken.None);
        var repeated = await environment.Service.AddPaymentAsync(order.Id, request, CancellationToken.None);

        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(1, await environment.DbContext.OrderPayments.CountAsync());
        await Assert.ThrowsAsync<ConflictException>(() => environment.Service.AddPaymentAsync(
            order.Id,
            request with { Amount = 550_000 },
            CancellationToken.None));
    }

    private static UpdateOrderForm CreateUpdateForm(
        OrderDetailDto order,
        Product product,
        int quantity,
        decimal? unitPrice = null,
        UpdateOverrides? overrides = null) =>
        new()
        {
            Id = order.Id,
            RowVersion = order.RowVersion,
            OrdererName = overrides?.OrdererName ?? order.OrdererName,
            OrdererPhone = overrides?.OrdererPhone ?? order.OrdererPhone,
            ChannelId = order.ChannelId,
            RecipientName = overrides?.RecipientName ?? order.RecipientName,
            RecipientPhone = overrides?.ClearRecipientPhone == true
                ? null
                : overrides?.RecipientPhone ?? order.RecipientPhone,
            PickupAtShop = order.PickupAtShop,
            ProvinceShipping = order.ProvinceShipping,
            DeliveryAddress = overrides?.DeliveryAddress ?? order.DeliveryAddress,
            DeliveryAddressDescription = overrides?.DeliveryAddressDescription ?? order.DeliveryAddressDescription,
            DeliveryLatitude = order.DeliveryLatitude,
            DeliveryLongitude = order.DeliveryLongitude,
            DeliveryAt = overrides?.DeliveryAt ?? order.DeliveryAt,
            DeliveryTo = overrides?.DeliveryTo ?? order.DeliveryTo,
            DepositAmount = overrides?.DepositAmount ?? order.DepositAmount,
            ShippingFee = overrides?.ShippingFee ?? order.ShippingFee,
            ShippingFeeActual = order.ShippingFeeActual,
            Description = overrides?.Description ?? order.Description,
            ContentNote = overrides?.ContentNote ?? order.ContentNote,
            Items = overrides?.Items?.ToList()
                ?? [CreateLine(order.Items.Single(), product, quantity, unitPrice ?? product.Price)],
            MaterialsSpecified = overrides?.Materials is not null,
            Materials = overrides?.Materials?.ToList() ?? []
        };

    private static OrderLineRequest CreateLine(
        OrderItemDto existing,
        Product product,
        int quantity,
        decimal unitPrice,
        string? note = null) =>
        new()
        {
            Id = existing.Id.ToString(),
            ProductId = product.Id.ToString(),
            ProductSku = product.Sku,
            ProductName = existing.ProductName,
            UnitPrice = unitPrice,
            Quantity = quantity,
            Note = note ?? existing.Note
        };

    private sealed record ProductSeed(string Sku, int Stock, bool TracksInventory, decimal Price);

    private sealed record OrderLineSeed(Product Product, int Quantity, string? Note = null);

    private sealed class UpdateOverrides
    {
        public string? OrdererName { get; init; }
        public string? OrdererPhone { get; init; }
        public string? RecipientName { get; init; }
        public string? RecipientPhone { get; init; }
        public bool ClearRecipientPhone { get; init; }
        public string? DeliveryAddress { get; init; }
        public string? DeliveryAddressDescription { get; init; }
        public DateTimeOffset? DeliveryAt { get; init; }
        public DateTimeOffset? DeliveryTo { get; init; }
        public decimal? DepositAmount { get; init; }
        public decimal? ShippingFee { get; init; }
        public string? Description { get; init; }
        public string? ContentNote { get; init; }
        public IReadOnlyCollection<OrderLineRequest>? Items { get; init; }
        public IReadOnlyCollection<SaveOrderMaterialItem>? Materials { get; init; }
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 8, 8, 1, 0, 0, TimeSpan.Zero);

        private readonly string _databaseName;

        private TestEnvironment(
            string databaseName,
            AppDbContext dbContext,
            IReadOnlyList<Product> products)
        {
            _databaseName = databaseName;
            DbContext = dbContext;
            Products = products;
            var fileStorage = new NullFileStorage();
            var httpContextAccessor = new HttpContextAccessor();
            var timeProvider = new FixedTimeProvider(Now);
            var inventoryService = new InventoryService(dbContext, fileStorage, httpContextAccessor, timeProvider);
            Service = new OrderService(dbContext, fileStorage, httpContextAccessor, timeProvider, inventoryService);
        }

        public AppDbContext DbContext { get; }

        public OrderService Service { get; }

        public IReadOnlyList<Product> Products { get; }

        public static async Task<TestEnvironment> CreateAsync(params ProductSeed[] productSeeds)
        {
            var databaseName = $"LamieOrderUpdate_{Guid.NewGuid():N}";
            var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var category = new Category(10);
            var productType = new ProductType("ORDER_TEST", 10);
            dbContext.Categories.Add(category);
            dbContext.ProductTypes.Add(productType);
            await dbContext.SaveChangesAsync();

            var products = productSeeds.Select(seed =>
                new Product(seed.Sku, seed.Price, seed.Stock, category.Id, productType.Id, seed.TracksInventory)).ToList();
            dbContext.Products.AddRange(products);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            return new TestEnvironment(databaseName, dbContext, products);
        }

        public async Task<Order> AddOrderAsync(
            IReadOnlyCollection<OrderLineSeed> lines,
            string? illustrationUrl = null)
        {
            var order = new Order(
                $"T-{Guid.NewGuid():N}",
                Channel.AdminId,
                null,
                new OrderDetails(
                    "Orderer",
                    "0900000001",
                    "Recipient",
                    "0911111111",
                    false,
                    false,
                    "123 Flower Street",
                    null,
                    10.7769m,
                    106.7009m,
                    Now.AddDays(1).UtcDateTime,
                    null,
                    0,
                    10,
                    null,
                    null,
                    "Original order note"),
                lines.Select(line => new OrderItemSnapshot(
                    line.Product.Id,
                    line.Product.Sku,
                    $"{line.Product.Sku} name",
                    null,
                    line.Product.Price,
                    line.Quantity,
                    Note: line.Note)),
                Now.UtcDateTime,
                null,
                "integration-test");
            if (!string.IsNullOrWhiteSpace(illustrationUrl))
                order.AddImage(order.Items.First().Id, illustrationUrl, 0);

            DbContext.Orders.Add(order);
            await DbContext.SaveChangesAsync();
            DbContext.ChangeTracker.Clear();
            return order;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.Database.EnsureDeletedAsync();
            await DbContext.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NullFileStorage : IFileStorage
    {
        public Task<string> UploadPublicAsync(
            Stream content,
            string objectPath,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"https://test.local/{objectPath}");

        public Task DeleteAsync(string? publicUrl, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
