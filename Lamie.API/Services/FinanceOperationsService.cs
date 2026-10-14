using System.Data;
using System.Security.Claims;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Finance;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class FinanceOperationsService : IFinanceOperationsService, IFinancePeriodGuard
{
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IHttpContextAccessor _http;

    public FinanceOperationsService(AppDbContext db, TimeProvider clock, IHttpContextAccessor http)
    {
        _db = db; _clock = clock; _http = http;
    }

    public async Task<IReadOnlyList<MaterialPurchaseDto>> ListPurchasesAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        ValidateRange(from, to);
        var query = _db.MaterialPurchases.AsNoTracking();
        if (from.HasValue) query = query.Where(x => x.ReceivedDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.ReceivedDate <= to.Value);
        var purchases = await query.OrderByDescending(x => x.ReceivedDate).ThenByDescending(x => x.CreatedAt).ToListAsync(ct);
        return await MapPurchasesAsync(purchases, ct);
    }

    public async Task<MaterialPurchaseDto> GetPurchaseAsync(Guid id, CancellationToken ct)
    {
        var purchase = await _db.MaterialPurchases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(MaterialPurchase), id);
        return (await MapPurchasesAsync([purchase], ct))[0];
    }

    public async Task<Guid> CreatePurchaseAsync(SaveMaterialPurchaseRequest request, CancellationToken ct)
    {
        ValidatePurchase(request);
        await InvalidateAsync(request.ReceivedDate, "Thêm phiếu mua nguyên liệu", ct);
        var now = UtcNow();
        var purchase = new MaterialPurchase(request.PurchaseDate, request.ReceivedDate, request.VendorName,
            request.VendorAddress, request.VendorTaxId, request.DocumentStatus, request.TaxEligibilityStatus,
            request.DocumentReference, request.AttachmentUrl, request.Notes, now, ActorId(), request.InvoiceDate);
        _db.MaterialPurchases.Add(purchase);
        _db.MaterialPurchaseLines.AddRange(request.Lines.Select(x =>
            new MaterialPurchaseLine(purchase.Id, x.Group, x.Description, x.Amount)));
        await _db.SaveChangesAsync(ct);
        return purchase.Id;
    }

    public async Task UpdatePurchaseAsync(Guid id, SaveMaterialPurchaseRequest request, CancellationToken ct)
    {
        ValidatePurchase(request);
        var purchase = await _db.MaterialPurchases.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(MaterialPurchase), id);
        var paid = await _db.MaterialPurchasePayments
            .Where(x => x.PurchaseId == id && x.Status == PaymentTransactionStatus.Succeeded)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var total = request.Lines.Sum(x => x.Amount);
        if (paid > total) throw new ConflictException("Tổng đã thanh toán không thể lớn hơn giá trị phiếu mua sau khi sửa.");
        await InvalidateAsync(purchase.ReceivedDate, "Sửa phiếu mua nguyên liệu", ct);
        if (purchase.ReceivedDate != request.ReceivedDate)
            await InvalidateAsync(request.ReceivedDate, "Chuyển ngày nhận phiếu mua nguyên liệu", ct);
        purchase.Update(request.PurchaseDate, request.ReceivedDate, request.VendorName, request.VendorAddress,
            request.VendorTaxId, request.DocumentStatus, request.TaxEligibilityStatus, request.DocumentReference,
            request.AttachmentUrl, request.Notes, UtcNow(), request.InvoiceDate);
        var oldLines = await _db.MaterialPurchaseLines.Where(x => x.PurchaseId == id).ToListAsync(ct);
        _db.MaterialPurchaseLines.RemoveRange(oldLines);
        _db.MaterialPurchaseLines.AddRange(request.Lines.Select(x => new MaterialPurchaseLine(id, x.Group, x.Description, x.Amount)));
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddPurchasePaymentAsync(Guid id, AddFinancePaymentRequest request, CancellationToken ct)
    {
        var purchase = await _db.MaterialPurchases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(MaterialPurchase), id);
        var payment = new MaterialPurchasePayment(id, request.Amount, request.PaidAt.UtcDateTime,
            request.Method, request.IdempotencyKey, request.Note, UtcNow(), ActorId());
        if (await PaymentExistsAsync(_db.MaterialPurchasePayments, payment.IdempotencyKey, id, payment.Amount, ct)) return;
        var total = await _db.MaterialPurchaseLines.Where(x => x.PurchaseId == id).SumAsync(x => x.Amount, ct);
        var paid = await _db.MaterialPurchasePayments.Where(x => x.PurchaseId == id && x.Status == PaymentTransactionStatus.Succeeded)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        if (paid + request.Amount > total) throw new ConflictException("Thanh toán vượt quá số còn nợ của phiếu mua.");
        await InvalidateAsync(LocalDate(payment.PaidAt), "Thanh toán phiếu mua nguyên liệu", ct);
        _db.MaterialPurchasePayments.Add(payment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddExpensePaymentAsync(Guid id, AddFinancePaymentRequest request, CancellationToken ct)
    {
        var expense = await _db.Expenses.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(nameof(Expense), id);
        var payment = new ExpensePayment(id, request.Amount, request.PaidAt.UtcDateTime,
            request.Method, request.IdempotencyKey, request.Note, UtcNow(), ActorId());
        if (await PaymentExistsAsync(_db.ExpensePayments, payment.IdempotencyKey, id, payment.Amount, ct)) return;
        var paid = await _db.ExpensePayments.Where(x => x.ExpenseId == id && x.Status == PaymentTransactionStatus.Succeeded)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        if (paid == 0 && expense.PaidAt.HasValue)
            paid = expense.Amount;
        if (paid + request.Amount > expense.Amount) throw new ConflictException("Thanh toán vượt quá số còn nợ của chi phí.");
        await InvalidateAsync(LocalDate(payment.PaidAt), "Thanh toán chi phí vận hành", ct);
        _db.ExpensePayments.Add(payment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<FinancePeriodDto> GetOrCreatePeriodAsync(SaveFinancePeriodRequest request, CancellationToken ct)
    {
        ValidateRange(request.From, request.To);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var existing = await _db.FinancePeriods.SingleOrDefaultAsync(x => x.From == request.From && x.To == request.To, ct);
        if (existing is not null)
        {
            if (request.OpeningCashBalance.HasValue && existing.OpeningCashBalance != request.OpeningCashBalance)
            {
                var updateNow = UtcNow(); existing.UpdateOpeningCashBalance(request.OpeningCashBalance, updateNow);
                Event(existing, FinancePeriodEventKind.DataChanged, "Cập nhật số dư tiền đầu kỳ", updateNow);
                await _db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return await MapPeriodAsync(existing, ct);
        }
        if (await _db.FinancePeriods.AnyAsync(x => request.From <= x.To && request.To >= x.From, ct))
            throw new ConflictException("Khoảng kỳ tài chính bị trùng với một kỳ đã có.");
        var now = UtcNow();
        var period = new FinancePeriod(request.From, request.To, request.OpeningCashBalance, now, ActorId());
        _db.FinancePeriods.Add(period);
        _db.FinancePeriodEvents.Add(new FinancePeriodEvent(period.Id, FinancePeriodEventKind.Created, period.DataVersion, null, now, ActorId()));
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await MapPeriodAsync(period, ct);
    }

    public async Task<FinancePeriodDto> GetPeriodAsync(Guid id, CancellationToken ct)
    {
        var period = await PeriodAsync(id, ct);
        return await MapPeriodAsync(period, ct);
    }

    public async Task SaveStocktakeAsync(Guid id, SaveStocktakeRequest request, CancellationToken ct)
    {
        var period = await PeriodAsync(id, ct); period.EnsureEditable();
        if (request.Lines.Count == 0 || request.Lines.GroupBy(x => x.Group).Any(g => g.Count() > 1))
            throw Validation("Lines", "Mỗi nhóm nguyên liệu phải có đúng một dòng kiểm kê.");
        var purchases = await PurchaseAmountsAsync(period.From, period.To, ct);
        var previous = await PreviousEndingAsync(period.From, ct);
        foreach (var line in request.Lines)
        {
            if (!Enum.IsDefined(line.Group)) throw Validation("Lines", "Nhóm nguyên liệu không hợp lệ.");
            if (previous.TryGetValue(line.Group, out var carried) && carried != line.OpeningValue)
                throw Validation("Lines", $"Tồn đầu {line.Group} phải kế thừa {carried} từ kỳ trước.");
            var bought = purchases.GetValueOrDefault(line.Group);
            var consumption = line.OpeningValue + bought + line.AdjustmentValue - line.EndingValue;
            if (consumption < 0) throw Validation("Lines", $"Tiêu hao {line.Group} không được âm.");
            if (line.WasteValue > consumption) throw Validation("Lines", $"Hao hụt {line.Group} không được vượt tiêu hao.");
        }
        var now = UtcNow();
        period.Invalidate(now);
        await InvalidateLaterPeriodsAsync(period, "Kiểm kê kỳ trước thay đổi tồn đầu kế thừa", now, ct);
        var old = await _db.MaterialStocktakeLines.Where(x => x.PeriodId == id).ToListAsync(ct);
        _db.MaterialStocktakeLines.RemoveRange(old);
        _db.MaterialStocktakeLines.AddRange(request.Lines.Select(x => new MaterialStocktakeLine(id, x.Group,
            x.OpeningValue, x.EndingValue, x.WasteValue, x.AdjustmentValue, x.IsEstimated, x.Note, now)));
        Event(period, FinancePeriodEventKind.DataChanged, "Cập nhật kiểm kê", now);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ConfirmStocktakeAsync(Guid id, CancellationToken ct)
    {
        var period = await PeriodAsync(id, ct); period.EnsureEditable();
        var lines = await _db.MaterialStocktakeLines.Where(x => x.PeriodId == id).ToListAsync(ct);
        var required = new[] { MaterialGroup.FreshFlowers, MaterialGroup.Supplies, MaterialGroup.ContainersAndAccessories };
        if (required.Any(group => lines.All(x => x.Group != group))) throw new ConflictException("Kiểm kê phải có đủ ba nhóm nguyên liệu bắt buộc.");
        if (lines.Any(x => x.IsEstimated)) throw new ConflictException("Cần xác nhận giá trị thực tế cho mọi dòng kiểm kê trước khi chốt.");
        var now = UtcNow(); foreach (var line in lines) line.Confirm(now, ActorId());
        period.ConfirmStocktake(now); Event(period, FinancePeriodEventKind.StocktakeConfirmed, null, now);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ConfirmShippingAsync(Guid id, CancellationToken ct)
    {
        var period = await PeriodAsync(id, ct); period.EnsureEditable();
        if (await UnreconciledShippingCountAsync(period.From, period.To, ct) > 0)
            throw new ConflictException("Còn đơn hoàn tất chưa nhập phí giao hàng thực tế.");
        var now = UtcNow(); period.ConfirmShipping(now); Event(period, FinancePeriodEventKind.ShippingConfirmed, null, now);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ConfirmExpensesAsync(Guid id, CancellationToken ct)
    {
        var period = await PeriodAsync(id, ct); period.EnsureEditable();
        if (await _db.Expenses.AnyAsync(x => x.ExpenseDate >= period.From && x.ExpenseDate <= period.To && x.PaymentHistoryIncomplete, ct))
            throw new ConflictException("Còn chi phí cũ chưa bổ sung lịch sử thanh toán.");
        var now = UtcNow(); period.ConfirmExpenses(now); Event(period, FinancePeriodEventKind.ExpensesConfirmed, null, now);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClosePeriodAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var period = await PeriodAsync(id, ct);
        if (await UnreconciledShippingCountAsync(period.From, period.To, ct) > 0)
            throw new ConflictException("Còn phí giao hàng chưa đối soát.");
        if (await _db.Expenses.AnyAsync(x => x.ExpenseDate >= period.From && x.ExpenseDate <= period.To && x.PaymentHistoryIncomplete, ct))
            throw new ConflictException("Còn chi phí có lịch sử thanh toán chưa đầy đủ.");
        var now = UtcNow(); period.Close(now, ActorId()); Event(period, FinancePeriodEventKind.Closed, null, now);
        await _db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public async Task ReopenPeriodAsync(Guid id, ReopenFinancePeriodRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw Validation("Reason", "Lý do mở lại phải có ít nhất 5 ký tự.");
        var period = await PeriodAsync(id, ct);
        if (await _db.FinancePeriods.AnyAsync(x => x.From > period.To && x.Status == FinancePeriodStatus.Closed, ct))
            throw new ConflictException("Phải mở lại các kỳ đã chốt phía sau trước vì tồn đầu có thể bị ảnh hưởng.");
        var now = UtcNow(); period.Reopen(now); Event(period, FinancePeriodEventKind.Reopened, request.Reason.Trim(), now);
        await InvalidateLaterPeriodsAsync(period, "Kỳ trước được mở lại", now, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task InvalidateAsync(DateOnly affectedDate, string reason, CancellationToken ct)
    {
        var periods = await _db.FinancePeriods.Where(x => x.From <= affectedDate && x.To >= affectedDate).ToListAsync(ct);
        if (periods.Any(x => x.Status == FinancePeriodStatus.Closed))
            throw new ConflictException("Dữ liệu thuộc kỳ đã chốt. Hãy mở lại kỳ trước khi thay đổi.");
        var now = UtcNow();
        foreach (var period in periods)
        {
            period.Invalidate(now);
            Event(period, FinancePeriodEventKind.DataChanged, reason, now);
        }
    }

    public async Task<ManagerialFinanceSummaryDto> GetSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        ValidateRange(from, to);
        var (startUtc, endUtc) = UtcRange(from, to);
        var period = await _db.FinancePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.From == from && x.To == to, ct);
        var orders = await _db.Orders.AsNoTracking().Where(x => x.OrderStatus == OrderStatus.Completed && x.CompletedAt >= startUtc && x.CompletedAt < endUtc).ToListAsync(ct);
        var recognizedRefunds = await _db.OrderPayments.AsNoTracking()
            .Where(x => x.Kind == OrderPaymentKind.Refund
                && x.Status == PaymentTransactionStatus.Succeeded
                && x.AffectsRecognizedRevenue
                && x.PaidAt >= startUtc && x.PaidAt < endUtc)
            .ToListAsync(ct);
        var productRevenue = orders.Sum(x => x.SubTotal - x.DiscountTotal)
            - recognizedRefunds.Sum(x => x.ProductRevenueAdjustment);
        var customerShipping = orders.Sum(x => x.ShippingFee)
            - recognizedRefunds.Sum(x => x.ShippingRevenueAdjustment);
        var carrier = await _db.Orders.AsNoTracking().Where(x => x.ShippingFeeIncurredAt >= startUtc && x.ShippingFeeIncurredAt < endUtc && x.OrderStatus != OrderStatus.Cancelled)
            .SumAsync(x => x.ShippingFeeActual ?? 0, ct);
        var operating = await _db.Expenses.AsNoTracking().Where(x => x.ExpenseDate >= from && x.ExpenseDate <= to).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var purchases = await PurchaseAmountsAsync(from, to, ct);
        var stock = period is null ? [] : await _db.MaterialStocktakeLines.AsNoTracking().Where(x => x.PeriodId == period.Id).ToListAsync(ct);
        var materials = Enum.GetValues<MaterialGroup>().Select(group =>
        {
            var line = stock.SingleOrDefault(x => x.Group == group); var bought = purchases.GetValueOrDefault(group);
            var consumption = line is null ? 0 : line.OpeningValue + bought + line.AdjustmentValue - line.EndingValue;
            return new MaterialGroupSummaryDto(group, line?.OpeningValue ?? 0, bought, line?.EndingValue ?? 0,
                consumption, line?.WasteValue ?? 0, consumption - (line?.WasteValue ?? 0), line?.IsEstimated ?? true);
        }).Where(x => x.OpeningValue != 0 || x.PurchasesValue != 0 || x.EndingValue != 0 || x.WasteValue != 0).ToList();
        var cogs = materials.Sum(x => x.ProductCogsValue); var waste = materials.Sum(x => x.WasteValue);
        var profit = productRevenue + customerShipping - cogs - waste - carrier - operating;
        var revenue = productRevenue + customerShipping;
        var cashIn = await _db.OrderPayments.AsNoTracking().Where(x => x.PaidAt >= startUtc && x.PaidAt < endUtc && x.Status == PaymentTransactionStatus.Succeeded && x.Kind != OrderPaymentKind.Refund)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var customerRefunds = await _db.OrderPayments.AsNoTracking().Where(x => x.PaidAt >= startUtc && x.PaidAt < endUtc && x.Status == PaymentTransactionStatus.Succeeded && x.Kind == OrderPaymentKind.Refund)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var purchasePaid = await _db.MaterialPurchasePayments.AsNoTracking().Where(x => x.PaidAt >= startUtc && x.PaidAt < endUtc && x.Status == PaymentTransactionStatus.Succeeded).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var expensePaid = await _db.ExpensePayments.AsNoTracking().Where(x => x.PaidAt >= startUtc && x.PaidAt < endUtc && x.Status == PaymentTransactionStatus.Succeeded).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        expensePaid += await _db.Expenses.AsNoTracking().Where(x => x.PaidAt >= startUtc && x.PaidAt < endUtc && !_db.ExpensePayments.Any(payment => payment.ExpenseId == x.Id)).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var shippingPaid = await _db.Orders.AsNoTracking().Where(x => x.ShippingFeePaidAt >= startUtc && x.ShippingFeePaidAt < endUtc && x.OrderStatus != OrderStatus.Cancelled).SumAsync(x => x.ShippingFeeActual ?? 0, ct);
        var purchaseTotal = await PurchaseTotalAsync(from, to, ct);
        var purchasePaymentsForDebt = await PaymentsForPurchasesReceivedAsync(from, to, ct);
        var expensePaymentsForDebt = await PaymentsForExpensesAsync(from, to, ct);
        var docs = await (from purchase in _db.MaterialPurchases.AsNoTracking()
                          join line in _db.MaterialPurchaseLines.AsNoTracking() on purchase.Id equals line.PurchaseId
                          where purchase.ReceivedDate >= @from && purchase.ReceivedDate <= to
                          group line by purchase.DocumentStatus into g
                          select new FinanceDocumentSummaryDto(g.Key, g.Select(x => x.PurchaseId).Distinct().Count(), g.Sum(x => x.Amount))).ToListAsync(ct);
        var reasons = new List<string>();
        if (period is null) reasons.Add("Chưa tạo kỳ tài chính cho phạm vi này.");
        else if (!period.IsFullyConfirmed) reasons.Add("Các xác nhận của kỳ chưa đầy đủ hoặc đã mất hiệu lực.");
        if (stock.Count == 0) reasons.Add("Chưa có kiểm kê cuối kỳ.");
        var unreconciled = await UnreconciledShippingCountAsync(from, to, ct);
        if (unreconciled > 0) reasons.Add($"Còn {unreconciled} đơn chưa đối soát phí giao hàng.");
        var incomplete = await _db.Expenses.CountAsync(x => x.ExpenseDate >= from && x.ExpenseDate <= to && x.PaymentHistoryIncomplete, ct);
        if (incomplete > 0) reasons.Add($"Còn {incomplete} chi phí thiếu lịch sử thanh toán.");
        var opening = period?.OpeningCashBalance;
        var cashOut = purchasePaid + expensePaid + shippingPaid + customerRefunds;
        return new ManagerialFinanceSummaryDto(from, to, period?.Id, period?.Status, reasons.Count > 0, reasons,
            productRevenue, customerShipping, carrier, operating, cogs, waste, profit,
            revenue == 0 ? null : decimal.Round(profit / revenue * 100, 2, MidpointRounding.AwayFromZero),
            opening, cashIn, cashOut, opening.HasValue ? opening + cashIn - cashOut : null,
            Math.Max(0, purchaseTotal - purchasePaymentsForDebt), Math.Max(0, operating - expensePaymentsForDebt),
            materials, docs, false, "Chưa có cấu hình thuế đã xác nhận; hệ thống chỉ đối soát chứng từ, không tự tính hoặc nộp thuế.", unreconciled, incomplete);
    }

    private async Task<FinancePeriodDto> MapPeriodAsync(FinancePeriod period, CancellationToken ct)
    {
        var purchases = await PurchaseAmountsAsync(period.From, period.To, ct);
        var lines = await _db.MaterialStocktakeLines.AsNoTracking().Where(x => x.PeriodId == period.Id).OrderBy(x => x.Group).ToListAsync(ct);
        var history = await _db.FinancePeriodEvents.AsNoTracking().Where(x => x.PeriodId == period.Id).OrderByDescending(x => x.OccurredAt).ToListAsync(ct);
        return new FinancePeriodDto(period.Id, period.From, period.To, period.OpeningCashBalance, period.Status, period.DataVersion,
            period.StocktakeConfirmedVersion == period.DataVersion, period.ShippingConfirmedVersion == period.DataVersion,
            period.ExpensesConfirmedVersion == period.DataVersion, period.IsFullyConfirmed, Offset(period.ClosedAt),
            lines.Select(x => { var bought = purchases.GetValueOrDefault(x.Group); var used = x.OpeningValue + bought + x.AdjustmentValue - x.EndingValue;
                return new StocktakeLineDto(x.Id, x.Group, x.OpeningValue, bought, x.EndingValue, x.WasteValue,
                    x.AdjustmentValue, used, used - x.WasteValue, x.IsEstimated, x.Status, x.Note); }).ToList(),
            history.Select(x => new FinancePeriodEventDto(x.Id, x.Kind, x.DataVersion, x.Reason, Offset(x.OccurredAt)!.Value, x.ActorUserId)).ToList());
    }

    private async Task InvalidateLaterPeriodsAsync(FinancePeriod source, string reason, DateTime now, CancellationToken ct)
    {
        var later = await _db.FinancePeriods.Where(x => x.From > source.To).OrderBy(x => x.From).ToListAsync(ct);
        if (later.Any(x => x.Status == FinancePeriodStatus.Closed))
            throw new ConflictException("Có kỳ phía sau đã chốt; phải mở các kỳ đó trước khi thay đổi tồn chuyển tiếp.");
        foreach (var item in later)
        {
            item.Invalidate(now);
            Event(item, FinancePeriodEventKind.DataChanged, reason, now);
        }
    }

    private async Task<IReadOnlyList<MaterialPurchaseDto>> MapPurchasesAsync(IReadOnlyList<MaterialPurchase> purchases, CancellationToken ct)
    {
        var ids = purchases.Select(x => x.Id).ToList();
        var lines = await _db.MaterialPurchaseLines.AsNoTracking().Where(x => ids.Contains(x.PurchaseId)).ToListAsync(ct);
        var payments = await _db.MaterialPurchasePayments.AsNoTracking().Where(x => ids.Contains(x.PurchaseId)).OrderBy(x => x.PaidAt).ToListAsync(ct);
        return purchases.Select(x => { var purchaseLines = lines.Where(y => y.PurchaseId == x.Id).ToList(); var purchasePayments = payments.Where(y => y.PurchaseId == x.Id).ToList();
            var total = purchaseLines.Sum(y => y.Amount); var paid = purchasePayments.Where(y => y.Status == PaymentTransactionStatus.Succeeded).Sum(y => y.Amount);
            return new MaterialPurchaseDto(x.Id, x.PurchaseDate, x.ReceivedDate, x.VendorName, x.VendorAddress, x.VendorTaxId,
                x.DocumentStatus, x.TaxEligibilityStatus, x.DocumentReference, x.AttachmentUrl, x.Notes, total, paid, total - paid,
                purchaseLines.Select(y => new MaterialPurchaseLineDto(y.Id, y.Group, y.Description, y.Amount)).ToList(),
                purchasePayments.Select(y => new FinancePaymentDto(y.Id, y.Amount, Offset(y.PaidAt)!.Value, y.Method, y.Status, y.Note)).ToList(),
                Offset(x.CreatedAt)!.Value, Offset(x.UpdatedAt)!.Value) { InvoiceDate = x.InvoiceDate }; }).ToList();
    }

    private async Task<Dictionary<MaterialGroup, decimal>> PurchaseAmountsAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await (from p in _db.MaterialPurchases.AsNoTracking() join l in _db.MaterialPurchaseLines.AsNoTracking() on p.Id equals l.PurchaseId
               where p.ReceivedDate >= @from && p.ReceivedDate <= to group l by l.Group into g select new { g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
    private async Task<Dictionary<MaterialGroup, decimal>> PreviousEndingAsync(DateOnly from, CancellationToken ct)
    {
        var prior = await _db.FinancePeriods.AsNoTracking().Where(x => x.To < from && x.Status == FinancePeriodStatus.Closed).OrderByDescending(x => x.To).FirstOrDefaultAsync(ct);
        return prior is null ? [] : await _db.MaterialStocktakeLines.AsNoTracking().Where(x => x.PeriodId == prior.Id).ToDictionaryAsync(x => x.Group, x => x.EndingValue, ct);
    }
    private Task<FinancePeriod> PeriodAsync(Guid id, CancellationToken ct) => _db.FinancePeriods.SingleOrDefaultAsync(x => x.Id == id, ct)
        .ContinueWith(t => t.Result ?? throw new NotFoundException(nameof(FinancePeriod), id), ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private void Event(FinancePeriod p, FinancePeriodEventKind kind, string? reason, DateTime now) =>
        _db.FinancePeriodEvents.Add(new FinancePeriodEvent(p.Id, kind, p.DataVersion, reason, now, ActorId()));
    private async Task<int> UnreconciledShippingCountAsync(DateOnly from, DateOnly to, CancellationToken ct)
    { var (start, end) = UtcRange(from, to); return await _db.Orders.CountAsync(x => x.OrderStatus == OrderStatus.Completed && x.CompletedAt >= start && x.CompletedAt < end && !x.ShippingFeeActual.HasValue, ct); }
    private async Task<decimal> PurchaseTotalAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await (from p in _db.MaterialPurchases where p.ReceivedDate >= @from && p.ReceivedDate <= to join l in _db.MaterialPurchaseLines on p.Id equals l.PurchaseId select (decimal?)l.Amount).SumAsync(ct) ?? 0;
    private async Task<decimal> PaymentsForPurchasesReceivedAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await (from p in _db.MaterialPurchases where p.ReceivedDate >= @from && p.ReceivedDate <= to join x in _db.MaterialPurchasePayments on p.Id equals x.PurchaseId where x.Status == PaymentTransactionStatus.Succeeded select (decimal?)x.Amount).SumAsync(ct) ?? 0;
    private async Task<decimal> PaymentsForExpensesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var ledger = await (from e in _db.Expenses where e.ExpenseDate >= @from && e.ExpenseDate <= to join x in _db.ExpensePayments on e.Id equals x.ExpenseId where x.Status == PaymentTransactionStatus.Succeeded select (decimal?)x.Amount).SumAsync(ct) ?? 0;
        var legacy = await _db.Expenses.Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to && e.PaidAt.HasValue && !_db.ExpensePayments.Any(x => x.ExpenseId == e.Id)).SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
        return ledger + legacy;
    }
    private static async Task<bool> PaymentExistsAsync<T>(DbSet<T> set, string key, Guid parentId, decimal amount, CancellationToken ct) where T : class
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (typeof(T) == typeof(MaterialPurchasePayment))
        { var row = await set.Cast<MaterialPurchasePayment>().AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key.Trim(), ct); if (row is null) return false; if (row.PurchaseId != parentId || row.Amount != amount) throw new ConflictException("Idempotency key đã được dùng cho giao dịch khác."); return true; }
        var expense = await set.Cast<ExpensePayment>().AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key.Trim(), ct);
        if (expense is null) return false; if (expense.ExpenseId != parentId || expense.Amount != amount) throw new ConflictException("Idempotency key đã được dùng cho giao dịch khác."); return true;
    }
    private static void ValidatePurchase(SaveMaterialPurchaseRequest r)
    { if (r.Lines is null || r.Lines.Count == 0) throw Validation("Lines", "Phiếu mua phải có ít nhất một dòng."); if (r.Lines.Count > 100) throw Validation("Lines", "Phiếu mua không được quá 100 dòng."); }
    private static void ValidateRange(DateOnly? from, DateOnly? to) { if (from.HasValue && to.HasValue) ValidateRange(from.Value, to.Value); }
    private static void ValidateRange(DateOnly from, DateOnly to)
    { if (from == default || to == default || from > to) throw Validation("From", "Khoảng ngày không hợp lệ."); if (to.DayNumber - from.DayNumber > 366) throw Validation("To", "Khoảng ngày không được quá 367 ngày."); }
    private static ValidationException Validation(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;
    private Guid? ActorId() { var value = _http.HttpContext?.User.FindFirstValue("sub") ?? _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier); return Guid.TryParse(value, out var id) ? id : null; }
    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(utc.Add(BusinessOffset));
    private static (DateTime Start, DateTime End) UtcRange(DateOnly from, DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), BusinessOffset).UtcDateTime, new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).UtcDateTime);
    private static DateTimeOffset? Offset(DateTime? value) => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
}
