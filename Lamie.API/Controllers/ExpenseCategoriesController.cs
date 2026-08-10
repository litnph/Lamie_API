using Lamie.Application.Expenses;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.ExpensesView)]
[Route("api/expense-categories")]
public sealed class ExpenseCategoriesController : ControllerBase
{
    private readonly IExpenseCategoryService _expenseCategoryService;

    public ExpenseCategoriesController(IExpenseCategoryService expenseCategoryService)
    {
        _expenseCategoryService = expenseCategoryService;
    }

    [HttpGet]
    public Task<IReadOnlyList<ExpenseCategoryDto>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        _expenseCategoryService.ListAsync(includeInactive, cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<ExpenseCategoryDto> Get(Guid id, CancellationToken cancellationToken) =>
        _expenseCategoryService.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> Create(
        CreateExpenseCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var id = await _expenseCategoryService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateExpenseCategoryRequest request,
        CancellationToken cancellationToken)
    {
        await _expenseCategoryService.UpdateAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _expenseCategoryService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
