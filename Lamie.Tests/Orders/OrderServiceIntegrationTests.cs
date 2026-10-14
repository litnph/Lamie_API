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
    public async Task DeliveryBoardOrdersLateUpcomingUnpaidAndCompletedBeforePagination()
    {
        var databaseName = $"LamieOrderPriority_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var now = new DateTimeOffset(2026, 10, 13, 3, 0, 0, TimeSpan.Zero);
            var createdAt = now.UtcDateTime.AddDays(-20);
            var late = CreateOrder("L261013-LATE", createdAt, now.UtcDateTime.AddHours(-1));
            var upcoming = CreateOrder("L261013-SOON", createdAt, now.UtcDateTime.AddHours(1));
            var unpaid = CreateOrder("L261013-UNPAID", createdAt, now.UtcDateTime.AddDays(-2));
            MoveToDelivered(unpaid, createdAt.AddHours(1));
            var completed = CreateOrder("L261013-DONE", createdAt, now.UtcDateTime.AddDays(-5));
            completed.ChangeStatus(OrderStatus.Producing, false, createdAt.AddHours(1), null, "integration-test");
            completed.ChangeStatus(OrderStatus.Ready, false, createdAt.AddHours(2), null, "integration-test");
            completed.ChangeStatus(OrderStatus.Shipping, false, createdAt.AddHours(3), null, "integration-test");
            completed.ChangePaymentStatus(PaymentStatus.Paid, createdAt.AddHours(4), null, "integration-test");
            completed.ChangeStatus(OrderStatus.Delivered, false, createdAt.AddHours(5), null, "integration-test");
            dbContext.Orders.AddRange(completed, unpaid, upcoming, late);
            await dbContext.SaveChangesAsync();

            var service = new OrderService(
                dbContext,
                new RecordingFileStorage(),
                new HttpContextAccessor(),
                new FixedTimeProvider(now));
            var board = await service.DeliveryBoardAsync(
                new OrderListQuery { Page = 1, PageSize = 20 },
                CancellationToken.None);

            Assert.Equal(
                [late.Id, upcoming.Id, unpaid.Id, completed.Id],
                board.Items.Select(item => item.Id));
            Assert.Equal(["late", "today", "delivered-unpaid", "completed"], board.Items.Select(item => item.TrackingGroup));
            Assert.Equal(1, board.Summary.LateCount);
            Assert.Equal(1, board.Summary.CompletedCount);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

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
            earlier.ChangeStatus(OrderStatus.Confirmed, false, now.AddMinutes(1), null, "integration-test");
            earlier.ChangeStatus(OrderStatus.Producing, false, now.AddMinutes(2), null, "integration-test");
            earlier.UpdateChecklist(true, true, true, true, now.AddMinutes(3), null, "integration-test");
            earlier.ChangeStatus(OrderStatus.Ready, false, now.AddMinutes(4), null, "integration-test");
            earlier.ChangeStatus(OrderStatus.Shipping, false, now.AddMinutes(5), null, "integration-test");
            earlier.ChangePaymentStatus(PaymentStatus.Paid, now.AddMinutes(6), null, "integration-test");
            earlier.ChangeStatus(OrderStatus.Delivered, false, now.AddMinutes(7), null, "integration-test");
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
                "0900000001",
                "Recipient",
                "0911111111",
                false,
                provinceShipping,
                "123 Test Street",
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

    private static void MoveToDelivered(Order order, DateTime at)
    {
        order.ChangeStatus(OrderStatus.Producing, false, at, null, "integration-test");
        order.ChangeStatus(OrderStatus.Ready, false, at.AddMinutes(1), null, "integration-test");
        order.ChangeStatus(OrderStatus.Shipping, false, at.AddMinutes(2), null, "integration-test");
        order.ChangeStatus(OrderStatus.Delivered, false, at.AddMinutes(3), null, "integration-test");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

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
