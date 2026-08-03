using Lamie.API.Models.Orders;
using Lamie.API.Services;
using Lamie.Application.Identity;
using Lamie.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.OrdersView)]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet]
    public Task<PagedOrdersDto> List([FromQuery] OrderListQuery query, CancellationToken cancellationToken) =>
        _orderService.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<OrderDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        _orderService.GetAsync(id, cancellationToken);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> Create([FromForm] CreateOrderForm form, CancellationToken cancellationToken) =>
        _orderService.CreateAsync(form, cancellationToken);

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> Update(Guid id, [FromForm] UpdateOrderForm form, CancellationToken cancellationToken) =>
        _orderService.UpdateAsync(id, form, cancellationToken);

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        ChangeOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _orderService.ChangeStatusAsync(id, request.Status, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/payment-status")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public async Task<IActionResult> ChangePaymentStatus(
        Guid id,
        ChangePaymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _orderService.ChangePaymentStatusAsync(id, request.Status, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionNames.OrdersCancel)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _orderService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("calendar")]
    public Task<IReadOnlyList<OrderCalendarItemDto>> Calendar(
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken) =>
        _orderService.CalendarAsync(date, cancellationToken);

    [HttpGet("calendar/locations")]
    public Task<IReadOnlyList<OrderDeliveryLocationDto>> CalendarLocations(
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken) =>
        _orderService.CalendarLocationsAsync(date, cancellationToken);
}
