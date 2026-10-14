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
    private readonly IAuthorizationService _authorizationService;

    public OrdersController(IOrderService orderService, IAuthorizationService authorizationService)
    {
        _orderService = orderService;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    public Task<PagedOrdersDto> List([FromQuery] OrderListQuery query, CancellationToken cancellationToken) =>
        _orderService.ListAsync(query, cancellationToken);

    [HttpGet("delivery-board")]
    public Task<OrderDeliveryBoardDto> DeliveryBoard([FromQuery] OrderListQuery query, CancellationToken cancellationToken) =>
        _orderService.DeliveryBoardAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<OrderDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        _orderService.GetAsync(id, cancellationToken);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> Create([FromForm] CreateOrderForm form, CancellationToken cancellationToken) =>
        _orderService.CreateAsync(form, cancellationToken);

    [HttpPost("batch")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<BatchCreateOrdersDto> CreateBatch(
        [FromForm] BatchCreateOrdersForm form,
        CancellationToken cancellationToken) =>
        _orderService.CreateBatchAsync(form, cancellationToken);

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> Update(Guid id, [FromForm] UpdateOrderForm form, CancellationToken cancellationToken) =>
        _orderService.UpdateAsync(id, form, cancellationToken);

    [HttpPost("{id:guid}/actions/{workflowAction}")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public async Task<ActionResult<OrderDetailDto>> RunWorkflowAction(
        Guid id,
        string workflowAction,
        OrderWorkflowCommand command,
        CancellationToken cancellationToken)
    {
        if (string.Equals(workflowAction, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            var allowed = await _authorizationService.AuthorizeAsync(User, null, PermissionNames.OrdersCancel);
            if (!allowed.Succeeded) return Forbid();
        }
        return await _orderService.RunWorkflowActionAsync(id, workflowAction, command, cancellationToken);
    }

    [HttpPatch("{id:guid}/recipient")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> UpdateRecipient(Guid id, UpdateOrderRecipientRequest request, CancellationToken cancellationToken) =>
        _orderService.UpdateRecipientAsync(id, request, cancellationToken);

    [HttpPatch("{id:guid}/assignment")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> UpdateAssignment(Guid id, UpdateOrderAssignmentRequest request, CancellationToken cancellationToken) =>
        _orderService.UpdateAssignmentAsync(id, request, cancellationToken);

    [HttpPatch("{id:guid}/checklist")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> UpdateChecklist(Guid id, UpdateOrderChecklistRequest request, CancellationToken cancellationToken) =>
        _orderService.UpdateChecklistAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/hold")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> Hold(Guid id, HoldOrderRequest request, CancellationToken cancellationToken) =>
        _orderService.HoldAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/resume")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<OrderDetailDto> Resume(Guid id, ResumeOrderRequest request, CancellationToken cancellationToken) =>
        _orderService.ResumeAsync(id, request, cancellationToken);

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

    [HttpPatch("{id:guid}/status/previous")]
    [Authorize(Policy = PermissionNames.OrdersReverseStatus)]
    public async Task<IActionResult> ReturnToPreviousStatus(
        Guid id,
        ReturnOrderToPreviousStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _orderService.ReturnToPreviousStatusAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/status/override")]
    [Authorize(Policy = PermissionNames.OrdersChangeStatus)]
    public Task<OrderDetailDto> OverrideStatus(
        Guid id,
        OverrideOrderStatusRequest request,
        CancellationToken cancellationToken) =>
        _orderService.OverrideStatusAsync(id, request, cancellationToken);

    [HttpPatch("{id:guid}/payment-status")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public async Task<IActionResult> ChangePaymentStatus(
        Guid id,
        ChangePaymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _orderService.ChangePaymentStatusAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/payments")]
    public Task<IReadOnlyList<OrderPaymentDto>> Payments(Guid id, CancellationToken cancellationToken) =>
        _orderService.ListPaymentsAsync(id, cancellationToken);

    [HttpPost("{id:guid}/payments")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public async Task<ActionResult<OrderPaymentDto>> AddPayment(
        Guid id,
        CreateOrderPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var payment = await _orderService.AddPaymentAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(Payments), new { id }, payment);
    }

    [HttpPatch("{id:guid}/shipping-fee-actual")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public async Task<IActionResult> UpdateShippingFeeActual(
        Guid id,
        UpdateShippingFeeActualRequest request,
        CancellationToken cancellationToken)
    {
        await _orderService.UpdateShippingFeeActualAsync(id, request, cancellationToken);
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
