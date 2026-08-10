using Lamie.Application.Expenses;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.ExpensesView)]
[Route("api/expenses")]
public sealed class ExpensesController : ControllerBase
{
    private readonly IExpenseService _expenseService;

    public ExpensesController(IExpenseService expenseService)
    {
        _expenseService = expenseService;
    }

    [HttpGet]
    public Task<PagedExpensesDto> List(
        [FromQuery] ExpenseListQuery query,
        CancellationToken cancellationToken) =>
        _expenseService.ListAsync(query, cancellationToken);

    [HttpGet("summary")]
    public Task<ExpenseSummaryDto> Summary(
        [FromQuery] ExpenseSummaryQuery query,
        CancellationToken cancellationToken) =>
        _expenseService.GetSummaryAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<ExpenseDto> Get(Guid id, CancellationToken cancellationToken) =>
        _expenseService.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> Create(
        CreateExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var id = await _expenseService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateExpenseRequest request,
        CancellationToken cancellationToken)
    {
        await _expenseService.UpdateAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _expenseService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
