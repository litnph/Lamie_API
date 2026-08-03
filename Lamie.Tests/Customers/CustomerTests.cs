using System.Text.Json;
using Lamie.API.Controllers;
using Lamie.API.Services;
using Lamie.Application.Customers;
using Lamie.Application.Identity;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Lamie.Tests.Customers;

public sealed class CustomerTests
{
    [Fact]
    public void ControllerExposesCustomerReadModelsAndProtectedNotesMutation()
    {
        var controllerPolicy = Assert.Single(
            typeof(CustomersController).GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.CustomersView, controllerPolicy?.Policy);

        AssertRoute(nameof(CustomersController.List), typeof(HttpGetAttribute), null);
        AssertRoute(nameof(CustomersController.Get), typeof(HttpGetAttribute), "{id:guid}");
        AssertRoute(nameof(CustomersController.GetOrderNotes), typeof(HttpGetAttribute), "{id:guid}/order-notes");
        AssertRoute(
            nameof(CustomersController.UpdateNotes),
            typeof(HttpPatchAttribute),
            "{id:guid}/notes",
            PermissionNames.CustomersManage);
    }

    [Theory]
    [InlineData(PaymentStatus.Paid, OrderStatus.Created, true)]
    [InlineData(PaymentStatus.Paid, OrderStatus.Completed, true)]
    [InlineData(PaymentStatus.Paid, OrderStatus.Cancelled, false)]
    [InlineData(PaymentStatus.Deposited, OrderStatus.Completed, false)]
    public void SpendingRuleMatchesExistingFrontendSemantics(
        PaymentStatus paymentStatus,
        OrderStatus orderStatus,
        bool expected) =>
        Assert.Equal(expected, CustomerService.CountsAsSpending(paymentStatus, orderStatus));

    [Fact]
    public void CustomerNotesCanBeReplacedWithoutChangingIdentity()
    {
        var baseline = new DateTime(2026, 7, 28, 1, 0, 0, DateTimeKind.Utc);
        var customer = new Customer("Nguyen Van A", "+84 912 345 678", "a@example.test", null, baseline);

        customer.Update(
            customer.Name,
            customer.Phone,
            customer.Email,
            "Prefers morning delivery",
            customer.IsActive,
            baseline.AddMinutes(1));

        Assert.Equal("84912345678", customer.NormalizedPhone);
        Assert.Equal("Prefers morning delivery", customer.Notes);
        Assert.Equal(baseline.AddMinutes(1), customer.UpdatedAt);
    }

    [Fact]
    public void CustomerPagedResponseUsesFrontendJsonShape()
    {
        var latestOrder = new OrderListItemDto(
            Guid.NewGuid(),
            "ORD-20260728-CUSTOMER",
            "Orderer",
            "0900000000",
            Channel.AdminId,
            "Recipient",
            "0911111111",
            false,
            false,
            "Address",
            new DateTimeOffset(2026, 7, 29, 1, 0, 0, TimeSpan.Zero),
            null,
            0,
            10,
            null,
            100,
            110,
            PaymentStatus.Paid,
            OrderStatus.Created,
            new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero),
            null,
            null);
        var response = new PagedCustomersDto(
            [new CustomerSummaryDto(Guid.NewGuid(), "Customer", "0900000000", 2, 1, 110, latestOrder, latestOrder.CreatedAt)],
            1,
            1,
            20,
            1,
            false,
            false);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"totalSpent\":110", json);
        Assert.Contains("\"latestOrder\"", json);
        Assert.Contains("\"lastPurchaseAt\"", json);
        Assert.Contains("\"hasNext\":false", json);
    }

    private static void AssertRoute(
        string methodName,
        Type attributeType,
        string? template,
        string? policy = null)
    {
        var method = typeof(CustomersController).GetMethod(methodName);
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
