using Lamie.Application.Identity;
using Lamie.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.ReportsView)]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IFinancialReportService _financialReportService;
    private readonly IFinancialReportExportService _financialReportExportService;

    public ReportsController(
        IFinancialReportService financialReportService,
        IFinancialReportExportService financialReportExportService)
    {
        _financialReportService = financialReportService;
        _financialReportExportService = financialReportExportService;
    }

    [HttpGet("financial")]
    public Task<FinancialReportDto> Financial(
        [FromQuery] FinancialReportQuery query,
        CancellationToken cancellationToken) =>
        _financialReportService.GetAsync(query, cancellationToken);

    [HttpGet("financial/export/excel")]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] FinancialReportQuery query,
        CancellationToken cancellationToken)
    {
        var report = await _financialReportService.GetAsync(query, cancellationToken);
        var file = _financialReportExportService.CreateExcel(report);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("financial/export/print")]
    public async Task<IActionResult> ExportPrint(
        [FromQuery] FinancialReportQuery query,
        CancellationToken cancellationToken)
    {
        var report = await _financialReportService.GetAsync(query, cancellationToken);
        var file = _financialReportExportService.CreatePrintableHtml(report);
        return File(file.Content, file.ContentType);
    }
}
