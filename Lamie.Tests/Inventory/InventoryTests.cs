using Lamie.API.Models.Inventory;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Inventory;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Lamie.Tests.Inventory;

public sealed class InventoryTests
{
    private static readonly DateTime Baseline = new(2026, 8, 28, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StockUnitSupportsNoSizeDynamicSizeThresholdAndRejectsNegativeStock()
    {
        var inventoryItemId = Guid.NewGuid();
        var noSize = new InventoryStockUnit(inventoryItemId, null, 0, true, Baseline);
        var sized = new InventoryStockUnit(inventoryItemId, " 20cm ", 5, true, Baseline);

        Assert.Null(noSize.SizeName);
        Assert.Equal(InventoryStockStatus.OutOfStock, noSize.Status);
        Assert.Equal("20cm", sized.SizeName);
        sized.Add(5, Baseline.AddMinutes(1));
        Assert.Equal(InventoryStockStatus.Low, sized.Status);
        sized.Add(1, Baseline.AddMinutes(2));
        Assert.Equal(InventoryStockStatus.Normal, sized.Status);
        Assert.Throws<DomainException>(() => sized.Remove(7, Baseline.AddMinutes(3)));
    }

    [Fact]
    public void EfModelUsesInventoryTablesPrecisionUniqueKeysRowVersionAndReceiptExpenseTrace()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieInventoryModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var stockUnit = model.FindEntityType(typeof(InventoryStockUnit));
        var transaction = model.FindEntityType(typeof(StockTransaction));
        var expense = model.FindEntityType(typeof(Expense));

        Assert.Equal("inv_stock_units", stockUnit?.GetTableName());
        Assert.Equal("inv_stock_transactions", transaction?.GetTableName());
        Assert.True(stockUnit?.FindProperty(nameof(InventoryStockUnit.RowVersion))?.IsConcurrencyToken);
        Assert.Contains(stockUnit!.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(item => item.Name).SequenceEqual([nameof(InventoryStockUnit.InventoryItemId), nameof(InventoryStockUnit.SizeKey)]));
        Assert.Contains(transaction!.GetIndexes(), index => index.IsUnique &&
            index.Properties.Single().Name == nameof(StockTransaction.OperationKey));
        Assert.Contains(expense!.GetIndexes(), index => index.IsUnique &&
            index.Properties.Single().Name == nameof(Expense.StockReceiptId));
        Assert.Empty(stockUnit.GetForeignKeys());
    }

    [Fact]
    public async Task ManualIssueAndAdjustmentAreIdempotentValidatedAndRecordedInLedger()
    {
        var databaseName = $"LamieInventoryMovement_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(Baseline));
            var unit = new MeasurementUnit("CAI", "Cái", "cái", false, true, Baseline);
            dbContext.MeasurementUnits.Add(unit);
            await dbContext.SaveChangesAsync();
            var inventory = new InventoryService(dbContext, new RecordingFileStorage(), new HttpContextAccessor(), clock);
            var item = await inventory.CreateItemAsync(new SaveInventoryItemRequest(
                "HOP_TEST", "Hộp kiểm thử", unit.Id, null, true,
                [new SaveInventoryStockUnitItem(null, null, 0, true)]), CancellationToken.None);
            var stockUnit = Assert.Single(item.StockUnits);
            await inventory.CreateReceiptAsync(new CreateStockReceiptForm
            {
                ClientRequestId = "movement-seed",
                ReceivedDate = new DateOnly(2026, 8, 28),
                Items = [new CreateStockReceiptItemForm { StockUnitId = stockUnit.Id, Quantity = 10 }]
            }, CancellationToken.None);
            stockUnit = Assert.Single(await inventory.GetItemStockUnitsAsync(item.Id, CancellationToken.None));

            var issueRequest = new CreateStockIssueRequest("manual-issue-1", 3, "Sử dụng nội bộ", stockUnit.RowVersion);
            var issue = await inventory.CreateIssueAsync(stockUnit.Id, issueRequest, CancellationToken.None);
            var repeatedIssue = await inventory.CreateIssueAsync(stockUnit.Id, issueRequest, CancellationToken.None);
            Assert.Equal(issue.Id, repeatedIssue.Id);
            Assert.Equal(-3, issue.QuantityDelta);
            Assert.Equal(7, issue.BalanceAfter);
            Assert.Equal(7, Assert.Single(await inventory.GetItemStockUnitsAsync(item.Id, CancellationToken.None)).Quantity);
            await Assert.ThrowsAsync<ConflictException>(() => inventory.CreateIssueAsync(
                stockUnit.Id,
                new CreateStockIssueRequest("manual-issue-too-large", 8, null, null),
                CancellationToken.None));

            var adjustment = await inventory.CreateAdjustmentAsync(
                stockUnit.Id,
                new CreateStockAdjustmentRequest("adjustment-1", 5, "Kiểm kê", null),
                CancellationToken.None);
            Assert.Equal(StockTransactionType.Adjustment, adjustment.Type);
            Assert.Equal(-2, adjustment.QuantityDelta);
            Assert.Equal(5, adjustment.BalanceAfter);
            Assert.Equal(5, Assert.Single(await inventory.GetItemStockUnitsAsync(item.Id, CancellationToken.None)).Quantity);

            var ledger = await inventory.ListTransactionsAsync(stockUnit.Id, 1, 20, CancellationToken.None);
            Assert.Equal(3, ledger.TotalCount);
            Assert.Contains(ledger.Items, transaction => transaction.Type == StockTransactionType.ManualIssue);
            Assert.Contains(ledger.Items, transaction => transaction.Type == StockTransactionType.Adjustment);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task IndependentNoSizeItemSupportsReceiptThresholdOrderDeltaAndReversal()
    {
        var databaseName = $"LamieInventoryNoSize_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(Baseline));
            var storage = new RecordingFileStorage();
            var http = new HttpContextAccessor();
            var unit = new MeasurementUnit("CAI", "Cái", "cái", false, true, Baseline);
            dbContext.MeasurementUnits.Add(unit);
            await dbContext.SaveChangesAsync();
            var inventory = new InventoryService(dbContext, storage, http, clock);
            var item = await inventory.CreateItemAsync(new SaveInventoryItemRequest(
                "HOP_GIAY", "Hộp giấy", unit.Id, null, true,
                [new SaveInventoryStockUnitItem(null, null, 3, true)]), CancellationToken.None);
            var stockUnit = Assert.Single(item.StockUnits);
            Assert.False(item.UsesSizes);
            Assert.Null(stockUnit.SizeName);
            Assert.Empty(await dbContext.Ingredients.ToListAsync());
            var initialTasks = await inventory.ListReplenishmentTasksAsync(
                new InventoryListQuery { Page = 1, PageSize = 20 }, CancellationToken.None);
            Assert.Equal(1, initialTasks.TotalCount);
            Assert.Equal(1, initialTasks.PendingCount);
            var filteredTasks = await inventory.ListReplenishmentTasksAsync(
                new InventoryListQuery { Search = "does-not-match", Page = 1, PageSize = 20 },
                CancellationToken.None);
            Assert.Equal(0, filteredTasks.TotalCount);
            Assert.Equal(1, filteredTasks.PendingCount);
            Assert.Equal(stockUnit.Id, Assert.Single(initialTasks.Items).StockUnitId);
            Assert.Equal(1, (await inventory.ListReplenishmentTasksAsync(
                new InventoryListQuery { Page = 1, PageSize = 20 }, CancellationToken.None)).TotalCount);

            await inventory.CreateReceiptAsync(new CreateStockReceiptForm
            {
                ClientRequestId = "no-size-receipt-1",
                ReceivedDate = new DateOnly(2026, 8, 29),
                Items = [new CreateStockReceiptItemForm { StockUnitId = stockUnit.Id, Quantity = 5 }]
            }, CancellationToken.None);
            var afterReceipt = Assert.Single(await inventory.GetItemStockUnitsAsync(item.Id, CancellationToken.None));
            Assert.Equal(5, afterReceipt.Quantity);
            Assert.Equal(InventoryStockStatus.Normal, afterReceipt.Status);
            Assert.Equal(0, (await inventory.ListReplenishmentTasksAsync(
                new InventoryListQuery { Page = 1, PageSize = 20 }, CancellationToken.None)).TotalCount);
            await Assert.ThrowsAsync<ConflictException>(() => inventory.UpdateItemAsync(item.Id,
                new SaveInventoryItemRequest(item.Code, item.Name, unit.Id, null, true,
                    [new SaveInventoryStockUnitItem(stockUnit.Id, "S", 3, true)]), CancellationToken.None));

            var order = CreateOrder(Baseline);
            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync();
            await inventory.SaveOrderMaterialsAsync(order.Id,
                new SaveOrderMaterialsRequest([new SaveOrderMaterialItem(null, stockUnit.Id, 2)], null), CancellationToken.None);
            var orders = new OrderService(dbContext, storage, http, clock, inventory);
            await orders.ChangeStatusAsync(order.Id, OrderStatus.Producing, CancellationToken.None);
            Assert.Equal(3, Assert.Single(await inventory.GetItemStockUnitsAsync(item.Id, CancellationToken.None)).Quantity);
            Assert.Equal(1, (await inventory.ListReplenishmentTasksAsync(
                new InventoryListQuery { Page = 1, PageSize = 20 }, CancellationToken.None)).TotalCount);

            var usage = Assert.Single(await inventory.GetOrderMaterialsAsync(order.Id, CancellationToken.None));
            await inventory.SaveOrderMaterialsAsync(order.Id,
                new SaveOrderMaterialsRequest([new SaveOrderMaterialItem(usage.Id, stockUnit.Id, 4)], null), CancellationToken.None);
            Assert.Equal(1, Assert.Single(await inventory.GetItemStockUnitsAsync(item.Id, CancellationToken.None)).Quantity);
            Assert.Equal(InventoryStockStatus.Low, Assert.Single(await inventory.GetItemStockUnitsAsync(item.Id, CancellationToken.None)).Status);
            Assert.Equal(1, (await inventory.ListReplenishmentTasksAsync(
                new InventoryListQuery { Page = 1, PageSize = 20 }, CancellationToken.None)).TotalCount);

            await orders.ChangeStatusAsync(order.Id, OrderStatus.Cancelled, CancellationToken.None);
            Assert.Equal(5, Assert.Single(await inventory.GetItemStockUnitsAsync(item.Id, CancellationToken.None)).Quantity);
            Assert.Equal(0, (await inventory.ListReplenishmentTasksAsync(
                new InventoryListQuery { Page = 1, PageSize = 20 }, CancellationToken.None)).TotalCount);
            Assert.Contains(await dbContext.StockTransactions.Where(transaction => transaction.OrderId == order.Id).ToListAsync(),
                transaction => transaction.Type == StockTransactionType.OrderReturn);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ReceiptExpenseAndOrderLifecycleAreAtomicIdempotentAndDeltaBased()
    {
        var databaseName = $"LamieInventory_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(Baseline));
            var storage = new RecordingFileStorage();
            var http = new HttpContextAccessor();
            var unit = new MeasurementUnit("CANH", "Cành", "cành", false, true, Baseline);
            dbContext.MeasurementUnits.Add(unit);
            await dbContext.SaveChangesAsync();
            var ingredient = new Ingredient("HOA_HONG", "Hoa hồng", unit.Id, null, true, Baseline);
            var category = new ExpenseCategory("Nguyên liệu", null, 10, true, Baseline);
            dbContext.Ingredients.Add(ingredient);
            dbContext.ExpenseCategories.Add(category);
            await dbContext.SaveChangesAsync();

            var inventory = new InventoryService(dbContext, storage, http, clock);
            var configured = await inventory.SaveIngredientStockUnitsAsync(
                ingredient.Id,
                new SaveIngredientStockUnitsRequest([
                    new SaveInventoryStockUnitItem(null, "20cm", 3, true),
                    new SaveInventoryStockUnitItem(null, "30cm", 2, true)
                ]),
                CancellationToken.None);
            var stockUnit = configured.Single(item => item.SizeName == "20cm");

            var receiptForm = new CreateStockReceiptForm
            {
                ClientRequestId = "receipt-idempotency-1",
                ReceivedDate = new DateOnly(2026, 8, 28),
                Note = "Nhập hoa buổi sáng",
                Items = [new CreateStockReceiptItemForm { StockUnitId = stockUnit.Id, Quantity = 10 }]
            };
            var receipt = await inventory.CreateReceiptAsync(receiptForm, CancellationToken.None);
            var repeated = await inventory.CreateReceiptAsync(receiptForm, CancellationToken.None);
            Assert.Equal(receipt.Id, repeated.Id);
            Assert.Equal(10, (await inventory.GetIngredientStockUnitsAsync(ingredient.Id, CancellationToken.None))
                .Single(item => item.Id == stockUnit.Id).Quantity);
            Assert.Single(await dbContext.StockTransactions.Where(item => item.ReceiptId == receipt.Id).ToListAsync());

            var pdfBytes = "%PDF-1.4 test"u8.ToArray();
            var invoice = new FormFile(new MemoryStream(pdfBytes), 0, pdfBytes.Length, "invoice", "invoice.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            };
            var costReceipt = await inventory.CreateReceiptAsync(new CreateStockReceiptForm
            {
                ClientRequestId = "receipt-expense-1",
                ReceivedDate = new DateOnly(2026, 8, 28),
                Items = [new CreateStockReceiptItemForm { StockUnitId = stockUnit.Id, Quantity = 2 }],
                Invoice = invoice,
                CreateExpense = true,
                ExpenseCategoryId = category.Id,
                ExpenseAmount = 250000
            }, CancellationToken.None);
            var expense = await dbContext.Expenses.SingleAsync(item => item.StockReceiptId == costReceipt.Id);
            Assert.Equal(costReceipt.Id, expense.StockReceiptId);
            Assert.NotNull(costReceipt.InvoiceUrl);
            Assert.Contains("inventory/receipts", storage.UploadedPaths.Single());

            var order = CreateOrder(Baseline);
            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync();
            var saved = await inventory.SaveOrderMaterialsAsync(order.Id, new SaveOrderMaterialsRequest([
                new SaveOrderMaterialItem(null, stockUnit.Id, 4)
            ], null), CancellationToken.None);
            Assert.Equal(0, Assert.Single(saved).DeductedQuantity);
            Assert.Equal(12, (await inventory.GetIngredientStockUnitsAsync(ingredient.Id, CancellationToken.None)).Single(item => item.Id == stockUnit.Id).Quantity);

            var orders = new OrderService(dbContext, storage, http, clock, inventory);
            await orders.ChangeStatusAsync(order.Id, OrderStatus.Producing, CancellationToken.None);
            await orders.ChangeStatusAsync(order.Id, OrderStatus.Producing, CancellationToken.None);
            Assert.Equal(8, (await inventory.GetIngredientStockUnitsAsync(ingredient.Id, CancellationToken.None)).Single(item => item.Id == stockUnit.Id).Quantity);
            Assert.Single(await dbContext.StockTransactions.Where(item => item.OrderId == order.Id && item.Type == StockTransactionType.OrderUsage).ToListAsync());

            var material = Assert.Single(await inventory.GetOrderMaterialsAsync(order.Id, CancellationToken.None));
            await inventory.SaveOrderMaterialsAsync(order.Id, new SaveOrderMaterialsRequest([
                new SaveOrderMaterialItem(material.Id, stockUnit.Id, 6)
            ], null), CancellationToken.None);
            Assert.Equal(6, (await inventory.GetIngredientStockUnitsAsync(ingredient.Id, CancellationToken.None)).Single(item => item.Id == stockUnit.Id).Quantity);
            await inventory.SaveOrderMaterialsAsync(order.Id, new SaveOrderMaterialsRequest([
                new SaveOrderMaterialItem(material.Id, stockUnit.Id, 3)
            ], null), CancellationToken.None);
            Assert.Equal(9, (await inventory.GetIngredientStockUnitsAsync(ingredient.Id, CancellationToken.None)).Single(item => item.Id == stockUnit.Id).Quantity);

            await orders.ChangeStatusAsync(order.Id, OrderStatus.Cancelled, CancellationToken.None);
            Assert.Equal(12, (await inventory.GetIngredientStockUnitsAsync(ingredient.Id, CancellationToken.None)).Single(item => item.Id == stockUnit.Id).Quantity);
            Assert.Equal(0, (await inventory.GetOrderMaterialsAsync(order.Id, CancellationToken.None)).Single().DeductedQuantity);
            Assert.Contains(await dbContext.StockTransactions.Where(item => item.OrderId == order.Id).ToListAsync(), item => item.Type == StockTransactionType.OrderReturn);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task InsufficientStockRollsBackOrderMaterialDeduction()
    {
        var databaseName = $"LamieInventoryInsufficient_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(Baseline));
            var storage = new RecordingFileStorage();
            var unit = new MeasurementUnit("CAI", "Cái", "cái", false, true, Baseline);
            dbContext.MeasurementUnits.Add(unit);
            await dbContext.SaveChangesAsync();
            var ingredient = new Ingredient("GIAY_GOI", "Giấy gói", unit.Id, null, true, Baseline);
            dbContext.Ingredients.Add(ingredient);
            var order = CreateOrder(Baseline);
            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync();
            var inventory = new InventoryService(dbContext, storage, new HttpContextAccessor(), clock);
            var stockUnit = Assert.Single(await inventory.SaveIngredientStockUnitsAsync(ingredient.Id,
                new SaveIngredientStockUnitsRequest([new SaveInventoryStockUnitItem(null, null, 0, true)]), CancellationToken.None));
            await inventory.SaveOrderMaterialsAsync(order.Id,
                new SaveOrderMaterialsRequest([new SaveOrderMaterialItem(null, stockUnit.Id, 1)], null), CancellationToken.None);
            var orders = new OrderService(dbContext, storage, new HttpContextAccessor(), clock, inventory);

            await Assert.ThrowsAsync<ConflictException>(() => orders.ChangeStatusAsync(order.Id, OrderStatus.Producing, CancellationToken.None));
            dbContext.ChangeTracker.Clear();
            Assert.Equal(OrderStatus.Created, (await dbContext.Orders.SingleAsync(item => item.Id == order.Id)).OrderStatus);
            Assert.Equal(0, (await dbContext.InventoryStockUnits.SingleAsync(item => item.Id == stockUnit.Id)).Quantity);
            Assert.Empty(await dbContext.StockTransactions.Where(item => item.OrderId == order.Id).ToListAsync());
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConcurrentStockUpdatesAllowOnlyOneWriter()
    {
        var databaseName = $"LamieInventoryConcurrency_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        Guid stockUnitId;
        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var unit = new MeasurementUnit("BO", "Bó", "bó", false, true, Baseline);
            setup.MeasurementUnits.Add(unit);
            await setup.SaveChangesAsync();
            var ingredient = new Ingredient("LA_PHU", "Lá phụ", unit.Id, null, true, Baseline);
            setup.Ingredients.Add(ingredient);
            await setup.SaveChangesAsync();
            var inventoryItem = new InventoryItem(ingredient.Code, ingredient.Name, unit.Id, null, true, Baseline, ingredient.Id);
            setup.InventoryItems.Add(inventoryItem);
            var stockUnit = new InventoryStockUnit(inventoryItem.Id, null, 0, true, Baseline);
            stockUnit.Add(1, Baseline);
            setup.InventoryStockUnits.Add(stockUnit);
            await setup.SaveChangesAsync();
            stockUnitId = stockUnit.Id;
        }

        try
        {
            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(options);
            var firstUnit = await first.InventoryStockUnits.SingleAsync(item => item.Id == stockUnitId);
            var secondUnit = await second.InventoryStockUnits.SingleAsync(item => item.Id == stockUnitId);
            firstUnit.Remove(1, Baseline.AddMinutes(1));
            secondUnit.Remove(1, Baseline.AddMinutes(1));

            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

            await using var verification = new AppDbContext(options);
            Assert.Equal(0, (await verification.InventoryStockUnits.SingleAsync(item => item.Id == stockUnitId)).Quantity);
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static Order CreateOrder(DateTime now) => new(
        $"L{now:yyMMdd}-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
        Channel.AdminId,
        null,
        new OrderDetails("Orderer", "", "Recipient", "0900000000", false, false, null, null, null, null,
            now.AddDays(1), null, 0, 0, null, null, null),
        [new OrderItemSnapshot(null, null, "Custom bouquet", null, 100000, 1)],
        now,
        null,
        "inventory-test");

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class RecordingFileStorage : IFileStorage
    {
        public List<string> UploadedPaths { get; } = [];
        public Task<string> UploadPublicAsync(Stream content, string objectPath, string contentType, CancellationToken cancellationToken = default)
        {
            UploadedPaths.Add(objectPath);
            return Task.FromResult($"/uploads/{objectPath}");
        }
        public Task DeleteAsync(string? publicUrl, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
