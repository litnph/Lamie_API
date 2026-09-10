using System.Reflection;
using System.Text.Json;
using Lamie.API.Controllers;
using Lamie.API.Models.Orders;
using Lamie.API.Services;
using Lamie.API.Middlewares;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lamie.Tests.Orders;

public sealed class OrderApiContractTests
{
    [Fact]
    public void OrdererPhoneIsOptionalAtTheMultipartModelBindingBoundary()
    {
        var property = typeof(CreateOrderForm).GetProperty(nameof(CreateOrderForm.OrdererPhone));

        Assert.NotNull(property);
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property!).WriteState);
    }

    [Fact]
    public void UpdateAcceptsAnOptionalRowVersionForBackwardCompatibleConcurrencyChecks()
    {
        var property = typeof(UpdateOrderForm).GetProperty(nameof(UpdateOrderForm.RowVersion));

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property).WriteState);
    }

    [Fact]
    public void CreateAndUpdateAcceptThePaidCheckboxAtTheMultipartBoundary()
    {
        Assert.Equal(typeof(bool), typeof(CreateOrderForm).GetProperty(nameof(CreateOrderForm.IsPaid))?.PropertyType);
        Assert.Equal(typeof(bool?), typeof(UpdateOrderForm).GetProperty(nameof(UpdateOrderForm.IsPaid))?.PropertyType);
    }

    [Fact]
    public void ControllerExposesBatchRouteWithTheSameManagePolicyAsSingleCreate()
    {
        var controllerPolicy = Assert.Single(
            typeof(OrdersController).GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.OrdersView, controllerPolicy?.Policy);

        AssertRoute(nameof(OrdersController.List), typeof(HttpGetAttribute), null);
        AssertRoute(nameof(OrdersController.Get), typeof(HttpGetAttribute), "{id:guid}");
        AssertRoute(nameof(OrdersController.Create), typeof(HttpPostAttribute), null, PermissionNames.OrdersManage);
        AssertRoute(nameof(OrdersController.CreateBatch), typeof(HttpPostAttribute), "batch", PermissionNames.OrdersManage);
        AssertRoute(nameof(OrdersController.Update), typeof(HttpPutAttribute), "{id:guid}", PermissionNames.OrdersManage);
        AssertRoute(nameof(OrdersController.ChangeStatus), typeof(HttpPatchAttribute), "{id:guid}/status", PermissionNames.OrdersManage);
        AssertRoute(nameof(OrdersController.ChangePaymentStatus), typeof(HttpPatchAttribute), "{id:guid}/payment-status", PermissionNames.OrdersManage);
        AssertRoute(nameof(OrdersController.Delete), typeof(HttpDeleteAttribute), "{id:guid}", PermissionNames.OrdersCancel);
        AssertRoute(nameof(OrdersController.Calendar), typeof(HttpGetAttribute), "calendar");
        AssertRoute(nameof(OrdersController.CalendarLocations), typeof(HttpGetAttribute), "calendar/locations");
    }

    [Fact]
    public void BusinessDateUsesHoChiMinhUtcBoundaries()
    {
        var range = OrderService.BusinessDayUtc(new DateOnly(2026, 7, 28));

        Assert.Equal(new DateTime(2026, 7, 27, 17, 0, 0, DateTimeKind.Utc), range.FromUtc);
        Assert.Equal(new DateTime(2026, 7, 28, 17, 0, 0, DateTimeKind.Utc), range.ToUtc);
    }

    [Fact]
    public async Task BatchErrorEnvelopeIdentifiesTheOffendingClientDraft()
    {
        var failure = BatchOrderException.From(
            2,
            "draft-client-3",
            new ValidationException(new Dictionary<string, string[]> { ["items"] = ["Quantity is invalid."] }));
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw failure,
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("BATCH_ORDER_FAILED", root.GetProperty("code").GetString());
        Assert.Equal("draft-client-3", root.GetProperty("batchError").GetProperty("clientDraftId").GetString());
        Assert.Equal(2, root.GetProperty("batchError").GetProperty("index").GetInt32());
        Assert.Equal("Quantity is invalid.", root.GetProperty("batchError").GetProperty("message").GetString());
    }

    [Fact]
    public void PagedResponseSerializesFrontendPropertyNamesAndUtcDates()
    {
        var item = new OrderListItemDto(
            Guid.NewGuid(),
            "ORD-20260728-ABC123",
            "Orderer",
            "0900000000",
            Channel.AdminId,
            "Recipient",
            "0911111111",
            false,
            false,
            "Address",
            new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero),
            null,
            0,
            10,
            null,
            100,
            110,
            PaymentStatus.Unpaid,
            OrderStatus.Created,
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            "Handle with care",
            "https://test.local/order.jpg");
        var response = new PagedOrdersDto([item], 1, 1, 20, 1, false, false);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"totalCount\":1", json);
        Assert.Contains("\"hasNext\":false", json);
        Assert.Contains("\"orderStatus\":1", json);
        Assert.Contains("\"contentNote\":\"Handle with care\"", json);
        Assert.Contains("\"imageUrl\":\"https://test.local/order.jpg\"", json);
        Assert.Contains("+00:00", json);
    }

    private static void AssertRoute(
        string methodName,
        Type attributeType,
        string? template,
        string? policy = null)
    {
        var method = typeof(OrdersController).GetMethod(methodName);
        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(attributeType, true)) as HttpMethodAttribute;
        Assert.Equal(template, route?.Template);
        if (policy is not null)
        {
            var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
            Assert.Equal(policy, authorize?.Policy);
        }
    }
}
