using System.Security.Claims;
using System.Text.Json;
using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

/// <summary>
/// Báo cáo thu và công nợ (PBI-022/PBI-044) và báo cáo thu chi (PBI-023).
/// JSON và Excel dùng chung một service truy vấn nên số liệu luôn khớp nhau.
/// </summary>
[ApiController]
[Route("api/bao-cao")]
[Authorize(Policy = "TaiChinh")]
public sealed class ReportController(
    IDebtReportService debts,
    IIncomeReportService incomes,
    IReportExcelService excel,
    IAuditService audit,
    TimeProvider clock) : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    [HttpGet("thu-cong-no")]
    public async Task<IActionResult> GetDebt([FromQuery] DebtReportQuery query, CancellationToken ct)
    {
        try
        {
            return Ok(await debts.GetAsync(query, forExport: false, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError("INVALID", ex.Message));
        }
    }

    [HttpGet("thu-cong-no/excel")]
    public async Task<IActionResult> ExportDebt([FromQuery] DebtReportQuery query, CancellationToken ct)
    {
        try
        {
            var report = await debts.GetAsync(query with { Page = 1, PageSize = 0 }, forExport: true, ct);
            var bytes = excel.BuildDebt(report);
            var fileName = ReportFileNames.Debt(report, clock.GetUtcNow());

            await WriteAuditAsync("DebtReport", query, report.Rows.Items.Count, ct);
            return File(bytes, ExcelContentType, fileName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError("INVALID", ex.Message));
        }
    }

    [HttpGet("thu-chi")]
    public async Task<IActionResult> GetIncome([FromQuery] IncomeReportQuery query, CancellationToken ct)
    {
        try
        {
            return Ok(await incomes.GetAsync(query, forExport: false, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError("INVALID", ex.Message));
        }
    }

    [HttpGet("thu-chi/excel")]
    public async Task<IActionResult> ExportIncome([FromQuery] IncomeReportQuery query, CancellationToken ct)
    {
        try
        {
            var report = await incomes.GetAsync(query with { Page = 1, PageSize = 0 }, forExport: true, ct);
            var bytes = excel.BuildIncome(report);
            var fileName = ReportFileNames.Income(report, clock.GetUtcNow());

            await WriteAuditAsync("IncomeReport", query, report.Rows.Items.Count, ct);
            return File(bytes, ExcelContentType, fileName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError("INVALID", ex.Message));
        }
    }

    private async Task WriteAuditAsync<T>(string target, T filter, int rowCount, CancellationToken ct)
    {
        var filterText = JsonSerializer.Serialize(filter);
        if (filterText.Length > 300) filterText = filterText[..300] + "…";

        await audit.LogAsync(
            AuditAction.ExportReport,
            ActorName,
            ActorId,
            target,
            $"Xuất báo cáo thu và công nợ: bộ lọc {filterText}; số dòng {rowCount}",
            ct);
    }
}
