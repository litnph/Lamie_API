using Lamie.Application.Finance;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.ReportsView)]
[Route("api/finance")]
public sealed class FinanceController : ControllerBase
{
    private readonly IFinanceOperationsService _finance;
    public FinanceController(IFinanceOperationsService finance) => _finance = finance;

    [HttpGet("summary")]
    public Task<ManagerialFinanceSummaryDto> Summary([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) => _finance.GetSummaryAsync(from, to, ct);
    [HttpGet("purchases")]
    public Task<IReadOnlyList<MaterialPurchaseDto>> Purchases([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => _finance.ListPurchasesAsync(from, to, ct);
    [HttpGet("purchases/{id:guid}")]
    public Task<MaterialPurchaseDto> Purchase(Guid id, CancellationToken ct) => _finance.GetPurchaseAsync(id, ct);

    [HttpPost("purchases"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> CreatePurchase(SaveMaterialPurchaseRequest request, CancellationToken ct)
    { var id = await _finance.CreatePurchaseAsync(request, ct); return CreatedAtAction(nameof(Purchase), new { id }, new { id }); }
    [HttpPut("purchases/{id:guid}"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> UpdatePurchase(Guid id, SaveMaterialPurchaseRequest request, CancellationToken ct)
    { await _finance.UpdatePurchaseAsync(id, request, ct); return NoContent(); }
    [HttpPost("purchases/{id:guid}/payments"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> PayPurchase(Guid id, AddFinancePaymentRequest request, CancellationToken ct)
    { await _finance.AddPurchasePaymentAsync(id, request, ct); return NoContent(); }
    [HttpPost("expenses/{id:guid}/payments"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> PayExpense(Guid id, AddFinancePaymentRequest request, CancellationToken ct)
    { await _finance.AddExpensePaymentAsync(id, request, ct); return NoContent(); }

    [HttpPost("periods"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public Task<FinancePeriodDto> GetOrCreatePeriod(SaveFinancePeriodRequest request, CancellationToken ct) => _finance.GetOrCreatePeriodAsync(request, ct);
    [HttpGet("periods/{id:guid}")]
    public Task<FinancePeriodDto> Period(Guid id, CancellationToken ct) => _finance.GetPeriodAsync(id, ct);
    [HttpPut("periods/{id:guid}/stocktake"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> SaveStocktake(Guid id, SaveStocktakeRequest request, CancellationToken ct)
    { await _finance.SaveStocktakeAsync(id, request, ct); return NoContent(); }
    [HttpPost("periods/{id:guid}/confirm-stocktake"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> ConfirmStocktake(Guid id, CancellationToken ct) { await _finance.ConfirmStocktakeAsync(id, ct); return NoContent(); }
    [HttpPost("periods/{id:guid}/confirm-shipping"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> ConfirmShipping(Guid id, CancellationToken ct) { await _finance.ConfirmShippingAsync(id, ct); return NoContent(); }
    [HttpPost("periods/{id:guid}/confirm-expenses"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> ConfirmExpenses(Guid id, CancellationToken ct) { await _finance.ConfirmExpensesAsync(id, ct); return NoContent(); }
    [HttpPost("periods/{id:guid}/close"), Authorize(Policy = PermissionNames.ExpensesManage)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) { await _finance.ClosePeriodAsync(id, ct); return NoContent(); }
    [HttpPost("periods/{id:guid}/reopen"), Authorize(Policy = PermissionNames.FinancePeriodsReopen)]
    public async Task<IActionResult> Reopen(Guid id, ReopenFinancePeriodRequest request, CancellationToken ct)
    { await _finance.ReopenPeriodAsync(id, request, ct); return NoContent(); }
}
