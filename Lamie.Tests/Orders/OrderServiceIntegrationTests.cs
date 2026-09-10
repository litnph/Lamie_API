using Lamie.API.Services;
using Lamie.Application.Common.Storage;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lamie.Tests.Orders;

public sealed class OrderServiceIntegrationTests
{
    [Fact]
    public async Task DefaultListPrioritizesUnfinishedOrdersBeforeDeliveryTimeAndDeleteRemovesTheAggregate()
    {
        var databaseName = $"LamieOrderDelete_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var now = new DateTime(2026, 8, 1, 1, 0, 0, DateTimeKind.Utc);
            var later = CreateOrder("L260801-BBBB", now, now.AddDays(2));
            later.AddImage(later.Items.Single().Id, "https://test.local/orders/later-illustration.jpg", 0);
            var earlier = CreateOrder(
                "L260801-AAAA",
                now,
                now.AddDays(1),
                now.AddDays(1).AddHours(2),
                provinceShipping: true,
                contentNote: "Send by express carrier",
                thumbnailUrl: "https://test.local/products/thumbnail.jpg");
            earlier.AddImage(earlier.Items.Single().Id, "https://test.local/orders/illustration.jpg", 0);
            earlier.ChangeStatus(OrderStatus.Producing, false, now.AddMinutes(1), null, "integration-test");
            earlier.ChangeStatus(OrderStatus.Shipping, false, now.AddMinutes(2), null, "integration-test");
            earlier.ChangeStatus(OrderStatus.Completed, false, now.AddMinutes(3), null, "integration-test");
            dbContext.Orders.AddRange(later, earlier);
            await dbContext.SaveChangesAsync();

            var storage = new RecordingFileStorage();
            var service = new OrderService(dbContext, storage, new HttpContextAccessor(), TimeProvider.System);
            var page = await service.ListAsync(new OrderListQuery(), CancellationToken.None);

            Assert.Equal([later.Id, earlier.Id], page.Items.Select(item => item.Id));
            Assert.Equal(earlier.DeliveryTo, page.Items[1].DeliveryTo?.UtcDateTime);
            Assert.True(page.Items[1].ProvinceShipping);
            Assert.Equal("Send by express carrier", page.Items[1].ContentNote);
            Assert.Equal("https://test.local/products/thumbnail.jpg", page.Items[1].ImageUrl);
            Assert.Equal("https://test.local/orders/later-illustration.jpg", page.Items[0].ImageUrl);

            await service.DeleteAsync(earlier.Id, CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            Assert.False(await dbContext.Orders.AnyAsync(order => order.Id == earlier.Id));
            Assert.False(await dbContext.OrderItems.AnyAsync(item => item.OrderId == earlier.Id));
            Assert.False(await dbContext.OrderImages.AnyAsync(image => image.OrderId == earlier.Id));
            Assert.False(await dbContext.OrderChangeLogs.AnyAsync(log => log.OrderId == earlier.Id));
            Assert.Contains("https://test.local/orders/illustration.jpg", storage.DeletedUrls);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static Order CreateOrder(
        string code,
        DateTime now,
        DateTime deliveryAt,
        DateTime? deliveryTo = null,
        bool provinceShipping = false,
        string? contentNote = null,
        string? thumbnailUrl = null) =>
        new(
            code,
            Channel.AdminId,
            null,
            new OrderDetails(
                "Orderer",
                string.Empty,
                "Recipient",
                "0911111111",
                false,
                provinceShipping,
                null,
                null,
                null,
                null,
                deliveryAt,
                deliveryTo,
                0,
                0,
                null,
                null,
                contentNote),
            [new OrderItemSnapshot(null, null, "Custom bouquet", thumbnailUrl, 100, 1)],
            now,
            null,
            "integration-test");

    private sealed class RecordingFileStorage : IFileStorage
    {
        public List<string> DeletedUrls { get; } = [];

        public Task<string> UploadPublicAsync(
            Stream content,
            string objectPath,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult($"https://test.local/{objectPath}");

        public Task DeleteAsync(string? publicUrl, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(publicUrl))
                DeletedUrls.Add(publicUrl);
            return Task.CompletedTask;
        }
    }
}
