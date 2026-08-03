using Lamie.API.Services;
using Lamie.Application.Customers;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.CustomersView)]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    public Task<PagedCustomersDto> List(
        [FromQuery] CustomerListQuery query,
        CancellationToken cancellationToken) =>
        _customerService.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<CustomerDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        _customerService.GetAsync(id, cancellationToken);

    [HttpGet("{id:guid}/order-notes")]
    public Task<CustomerOrderNotesResultDto> GetOrderNotes(
        Guid id,
        CancellationToken cancellationToken) =>
        _customerService.GetOrderNotesAsync(id, cancellationToken);

    [HttpPatch("{id:guid}/notes")]
    [Authorize(Policy = PermissionNames.CustomersManage)]
    public Task<CustomerDetailDto> UpdateNotes(
        Guid id,
        UpdateCustomerNotesRequest request,
        CancellationToken cancellationToken) =>
        _customerService.UpdateNotesAsync(id, request, cancellationToken);
}
