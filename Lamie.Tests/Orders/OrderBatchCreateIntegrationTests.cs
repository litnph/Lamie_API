using Lamie.API.Models.Orders;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lamie.Tests.Orders;

public sealed class OrderBatchCreateIntegrationTests
{
    [Fact]
    public async Task EmptyBatchIsRejected()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            environment.Service.CreateBatchAsync(new BatchCreateOrdersForm(), CancellationToken.None));

        Assert.Contains("At least one order", exception.Errors["orders"].Single());
        Assert.Empty(await environment.DbContext.Orders.ToListAsync());
    }

    [Fact]
    public async Task DuplicateClientDraftIdsAreRejectedBeforeStartingCreation()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var request = new BatchCreateOrdersForm
        {
            Orders =
            [
                environment.CreateForm("same-draft", "0900000001"),
                environment.CreateForm("same-draft", "0900000002")
            ]
        };

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            environment.Service.CreateBatchAsync(request, CancellationToken.None));

        Assert.Contains("unique", exception.Errors["orders[1].clientDraftId"].Single());
        Assert.Empty(await environment.DbContext.Orders.ToListAsync());
    }

    [Fact]
    public async Task MultipleOrdersCommitTogetherWithUniqueNumbersDepositCardAndBanner()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var request = new BatchCreateOrdersForm
        {
            Orders =
            [
                environment.CreateForm("draft-1", "0900000001", deposit: 0),
                environment.CreateForm("draft-2", "0900000002", deposit: 125_000),
                environment.CreateForm(
                    "draft-3",
                    "0900000003",
                    deposit: 200_000,
                    hasCard: true,
                    cardMessage: "Chúc mừng sinh nhật",
                    hasBanner: true,
                    bannerMessage: "Happy Birthday")
            ]
        };

        var result = await environment.Service.CreateBatchAsync(request, CancellationToken.None);
        environment.DbContext.ChangeTracker.Clear();
        var orders = await environment.DbContext.Orders.Include(order => order.Items).OrderBy(order => order.RecipientPhone).ToListAsync();

        Assert.Equal(3, result.CreatedCount);
        Assert.Equal(["draft-1", "draft-2", "draft-3"], result.Orders.Select(order => order.ClientDraftId));
        Assert.Equal(3, result.Orders.Select(order => order.OrderNumber).Distinct().Count());
        Assert.Equal(3, orders.Count);
        Assert.Equal([0m, 125_000m, 200_000m], orders.Select(order => order.DepositAmount));
        Assert.Equal("Chúc mừng sinh nhật", orders[2].Items.Single().CardMessage);
        Assert.Equal("Happy Birthday", orders[2].Items.Single().BannerMessage);
    }

    [Fact]
    public async Task FailureInMiddleRollsBackEveryOrderAndCleansUploadedFiles()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var first = environment.CreateForm("draft-valid", "0900000011");
        first.Images.Add(CreateGif("first.gif", 0x11));
        var invalid = environment.CreateForm("draft-invalid", "0900000012", productId: "999999");
        var request = new BatchCreateOrdersForm
        {
            Orders = [first, invalid, environment.CreateForm("draft-never-committed", "0900000013")]
        };

        var exception = await Assert.ThrowsAsync<BatchOrderException>(() =>
            environment.Service.CreateBatchAsync(request, CancellationToken.None));
        environment.DbContext.ChangeTracker.Clear();

        Assert.Equal(1, exception.Index);
        Assert.Equal("draft-invalid", exception.ClientDraftId);
        Assert.Equal("NOT_FOUND", exception.CauseCode);
        Assert.Empty(await environment.DbContext.Orders.ToListAsync());
        Assert.Empty(await environment.DbContext.Customers.ToListAsync());
        Assert.Single(environment.Storage.UploadedUrls);
        Assert.Equal(environment.Storage.UploadedUrls, environment.Storage.DeletedUrls);
    }

    [Fact]
    public async Task AttachmentsStayWithTheirOwningOrder()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var first = environment.CreateForm("draft-image-1", "0900000021");
        first.Images.Add(CreateGif("one.gif", 0x21));
        var second = environment.CreateForm("draft-image-2", "0900000022");
        second.Images.Add(CreateGif("two.gif", 0x22));

        await environment.Service.CreateBatchAsync(
            new BatchCreateOrdersForm { Orders = [first, second] },
            CancellationToken.None);
        environment.DbContext.ChangeTracker.Clear();
        var orders = await environment.DbContext.Orders
            .Include(order => order.Images)
            .OrderBy(order => order.RecipientPhone)
            .ToListAsync();

        Assert.Contains("marker=21", orders[0].Images.Single().ImageUrl);
        Assert.Contains("marker=22", orders[1].Images.Single().ImageUrl);
        Assert.NotEqual(orders[0].Images.Single().OrderItemId, orders[1].Images.Single().OrderItemId);
    }

    [Fact]
    public async Task ExistingSingleCreateStillUsesTheSharedCreationCore()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        var created = await environment.Service.CreateAsync(
            environment.CreateForm(null, "0900000031"),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(created.OrderCode));
        Assert.Single(await environment.DbContext.Orders.ToListAsync());
    }

    private static CreateOrderImageForm CreateGif(string fileName, byte marker)
    {
        var bytes = "GIF89a"u8.ToArray().Concat(new byte[] { 0, 0, 0, 0, 0, marker }).ToArray();
        var stream = new MemoryStream(bytes);
        return new CreateOrderImageForm
        {
            ImageFile = new FormFile(stream, 0, bytes.Length, "imageFile", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/gif"
            },
            OrderItemIndex = 0,
            SortOrder = 0
        };
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        private static readonly DateTimeOffset Now = new(2026, 8, 12, 2, 0, 0, TimeSpan.Zero);
        private readonly string _databaseName;

        private TestEnvironment(string databaseName, AppDbContext dbContext, Product product, RecordingFileStorage storage)
        {
            _databaseName = databaseName;
            DbContext = dbContext;
            Product = product;
            Storage = storage;
            Service = new OrderService(
                dbContext,
                storage,
                new HttpContextAccessor(),
                new FixedTimeProvider(Now));
        }

        public AppDbContext DbContext { get; }
        public Product Product { get; }
        public RecordingFileStorage Storage { get; }
        public OrderService Service { get; }

        public static async Task<TestEnvironment> CreateAsync()
        {
            var databaseName = $"LamieOrderBatch_{Guid.NewGuid():N}";
            var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;
            var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            var category = new Category(10);
            var productType = new ProductType("BATCH_TEST", 10);
            dbContext.Categories.Add(category);
            dbContext.ProductTypes.Add(productType);
            await dbContext.SaveChangesAsync();
            var product = new Product("BCH1", 500_000, 25, category.Id, productType.Id, true);
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();
            return new TestEnvironment(databaseName, dbContext, product, new RecordingFileStorage());
        }

        public CreateOrderForm CreateForm(
            string? clientDraftId,
            string recipientPhone,
            decimal? deposit = 100_000,
            bool hasCard = false,
            string? cardMessage = null,
            bool hasBanner = false,
            string? bannerMessage = null,
            string? productId = null) =>
            new()
            {
                ClientDraftId = clientDraftId,
                OrdererName = $"Orderer {recipientPhone}",
                OrdererPhone = recipientPhone,
                ChannelId = Channel.AdminId,
                RecipientName = $"Recipient {recipientPhone}",
                RecipientPhone = recipientPhone,
                DeliveryAddress = "461 Phan Văn Trị",
                DeliveryAt = Now.AddDays(1),
                DepositAmount = deposit,
                ShippingFee = 50_000,
                Items =
                [
                    new OrderLineRequest
                    {
                        ProductId = productId ?? Product.Id.ToString(),
                        ProductSku = Product.Sku,
                        ProductName = "Batch bouquet",
                        UnitPrice = Product.Price,
                        Quantity = 1,
                        HasCard = hasCard,
                        CardMessage = cardMessage,
                        HasBanner = hasBanner,
                        BannerMessage = bannerMessage
                    }
                ]
            };

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

    private sealed class RecordingFileStorage : IFileStorage
    {
        public List<string> UploadedUrls { get; } = [];
        public List<string> DeletedUrls { get; } = [];

        public async Task<string> UploadPublicAsync(
            Stream content,
            string objectPath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var marker = memory.ToArray()[^1].ToString("X2");
            var url = $"https://test.local/{objectPath}?marker={marker}";
            UploadedUrls.Add(url);
            return url;
        }

        public Task DeleteAsync(string? publicUrl, CancellationToken cancellationToken = default)
        {
            if (publicUrl is not null) DeletedUrls.Add(publicUrl);
            return Task.CompletedTask;
        }
    }
}
