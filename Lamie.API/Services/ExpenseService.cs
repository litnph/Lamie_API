using Lamie.Application.Common.Exceptions;
using Lamie.Application.Expenses;
using Lamie.Application.Finance;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class ExpenseService : IExpenseService
{
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IFinancePeriodGuard? _financePeriodGuard;

    public ExpenseService(AppDbContext dbContext, TimeProvider timeProvider, IFinancePeriodGuard? financePeriodGuard = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _financePeriodGuard = financePeriodGuard;
    }

    public async Task<PagedExpensesDto> ListAsync(
        ExpenseListQuery query,
        CancellationToken cancellationToken)
    {
        ValidateListQuery(query);

        var expenses =
            from expense in _dbContext.Expenses.AsNoTracking()
            join category in _dbContext.ExpenseCategories.AsNoTracking()
                on expense.ExpenseCategoryId equals category.Id
            select new { Expense = expense, CategoryName = category.Name };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            expenses = expenses.Where(row =>
                row.Expense.Description.Contains(search) ||
                (row.Expense.Notes != null && row.Expense.Notes.Contains(search)) ||
                row.CategoryName.Contains(search));
        }

        if (query.ExpenseCategoryId.HasValue)
            expenses = expenses.Where(row => row.Expense.ExpenseCategoryId == query.ExpenseCategoryId.Value);
        if (query.From.HasValue)
            expenses = expenses.Where(row => row.Expense.ExpenseDate >= query.From.Value);
        if (query.To.HasValue)
            expenses = expenses.Where(row => row.Expense.ExpenseDate <= query.To.Value);

        var totalCount = await expenses.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)query.PageSize));
        var page = Math.Min(query.Page, totalPages);
        var rows = await expenses
            .OrderByDescending(row => row.Expense.ExpenseDate)
            .ThenByDescending(row => row.Expense.CreatedAt)
            .ThenByDescending(row => row.Expense.Id)
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.Expense.Id).ToList();
        var payments = await _dbContext.ExpensePayments.AsNoTracking()
            .Where(payment => ids.Contains(payment.ExpenseId)).OrderBy(payment => payment.PaidAt).ToListAsync(cancellationToken);

        return new PagedExpensesDto(
            rows.Select(row => ToDto(row.Expense, row.CategoryName, payments.Where(payment => payment.ExpenseId == row.Expense.Id).ToList())).ToList(),
            totalCount,
            page,
            query.PageSize,
            totalPages,
            page < totalPages,
            page > 1);
    }

    public async Task<ExpenseDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await (
                from expense in _dbContext.Expenses.AsNoTracking()
                join category in _dbContext.ExpenseCategories.AsNoTracking()
                    on expense.ExpenseCategoryId equals category.Id
                where expense.Id == id
                select new { Expense = expense, CategoryName = category.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Expense), id);

        var payments = await _dbContext.ExpensePayments.AsNoTracking()
            .Where(payment => payment.ExpenseId == id).OrderBy(payment => payment.PaidAt).ToListAsync(cancellationToken);
        return ToDto(row.Expense, row.CategoryName, payments);
    }

    public async Task<Guid> CreateAsync(
        CreateExpenseRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureCategoryCanBeUsedAsync(request.ExpenseCategoryId, null, cancellationToken);
        var now = UtcNow();
        var expense = new Expense(
            request.ExpenseCategoryId,
            request.ExpenseDate,
            request.Amount,
            request.Description,
            request.Notes,
            now,
            paidAtUtc: request.PaidAt?.UtcDateTime,
            paymentMethod: request.PaymentMethod,
            vendor: request.Vendor,
            paymentIdempotencyKey: request.PaymentIdempotencyKey);
        ExpensePayment? initialPayment = null;
        if (request.PaidAt.HasValue && request.PaymentMethod.HasValue)
        {
            initialPayment = new ExpensePayment(expense.Id, request.Amount, request.PaidAt.Value.UtcDateTime,
                request.PaymentMethod.Value, request.PaymentIdempotencyKey ?? $"expense-initial-{expense.Id:N}",
                "Thanh toán khi tạo chi phí", now, null);
        }
        if (_financePeriodGuard is not null)
        {
            await _financePeriodGuard.InvalidateAsync(request.ExpenseDate, "Thêm chi phí vận hành", cancellationToken);
            if (initialPayment is not null)
            {
                var paymentDate = LocalDate(initialPayment.PaidAt);
                if (paymentDate != request.ExpenseDate)
                    await _financePeriodGuard.InvalidateAsync(paymentDate, "Thanh toán chi phí vận hành", cancellationToken);
            }
        }
        _dbContext.Expenses.Add(expense);
        if (initialPayment is not null)
            _dbContext.ExpensePayments.Add(initialPayment);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return expense.Id;
    }

    public async Task UpdateAsync(
        Guid id,
        UpdateExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var expense = await _dbContext.Expenses.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(Expense), id);
        await EnsureCategoryCanBeUsedAsync(
            request.ExpenseCategoryId,
            expense.ExpenseCategoryId,
            cancellationToken);
        _ = new Expense(request.ExpenseCategoryId, request.ExpenseDate, request.Amount, request.Description,
            request.Notes, UtcNow(), expense.StockReceiptId, request.PaidAt?.UtcDateTime,
            request.PaymentMethod, request.Vendor, request.PaymentIdempotencyKey);
        var ledgerPayments = await _dbContext.ExpensePayments.AsNoTracking()
            .Where(payment => payment.ExpenseId == id && payment.Status == PaymentTransactionStatus.Succeeded)
            .ToListAsync(cancellationToken);
        var paidAmount = ledgerPayments.Sum(payment => payment.Amount);
        if (paidAmount > request.Amount)
            throw new ConflictException("Số tiền chi phí sau khi sửa không thể nhỏ hơn tổng đã thanh toán.");
        if (ledgerPayments.Count > 0
            && (request.PaidAt?.UtcDateTime != expense.PaidAt || request.PaymentMethod != expense.PaymentMethod))
            throw new ConflictException("Lịch sử thanh toán không thể sửa trực tiếp; hãy ghi giao dịch điều chỉnh riêng.");
        if (ledgerPayments.Count == 0 && expense.PaidAt.HasValue && request.Amount != expense.Amount)
            throw new ConflictException("Chi phí cũ đã thanh toán không thể đổi số tiền; hãy bổ sung lịch sử giao dịch trước.");
        if (_financePeriodGuard is not null)
        {
            await _financePeriodGuard.InvalidateAsync(expense.ExpenseDate, "Sửa chi phí vận hành", cancellationToken);
            if (expense.ExpenseDate != request.ExpenseDate)
                await _financePeriodGuard.InvalidateAsync(request.ExpenseDate, "Chuyển kỳ chi phí vận hành", cancellationToken);
        }

        expense.Update(
            request.ExpenseCategoryId,
            request.ExpenseDate,
            request.Amount,
            request.Description,
            request.Notes,
            UtcNow(),
            request.PaidAt?.UtcDateTime,
            request.PaymentMethod,
            request.Vendor,
            request.PaymentIdempotencyKey);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var expense = await _dbContext.Expenses.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(Expense), id);
        if (expense.StockReceiptId.HasValue)
            throw new ConflictException("Chi phí được tạo từ phiếu nhập kho không thể xóa riêng. Hãy giữ chứng từ để bảo toàn lịch sử đối soát.");
        if (expense.PaidAt.HasValue || await _dbContext.ExpensePayments.AnyAsync(
                payment => payment.ExpenseId == id, cancellationToken))
            throw new ConflictException("Chi phí đã có giao dịch thanh toán không thể xóa; hãy dùng giao dịch điều chỉnh để giữ lịch sử.");
        if (_financePeriodGuard is not null)
            await _financePeriodGuard.InvalidateAsync(expense.ExpenseDate, "Xóa chi phí vận hành", cancellationToken);
        _dbContext.Expenses.Remove(expense);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExpenseSummaryDto> GetSummaryAsync(
        ExpenseSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(BusinessOffset).DateTime);
        var (rangeStart, rangeEnd) = ResolveSummaryRange(query, today);
        var summaryRows = await (
                from expense in _dbContext.Expenses.AsNoTracking()
                join category in _dbContext.ExpenseCategories.AsNoTracking()
                    on expense.ExpenseCategoryId equals category.Id
                where expense.ExpenseDate >= rangeStart && expense.ExpenseDate <= rangeEnd
                group expense by new { category.Id, category.Name }
                into categoryExpenses
                select new
                {
                    categoryExpenses.Key.Id,
                    categoryExpenses.Key.Name,
                    TotalAmount = categoryExpenses.Sum(expense => expense.Amount),
                    ExpenseCount = categoryExpenses.Count()
                })
            .OrderByDescending(row => row.TotalAmount)
            .ThenBy(row => row.Name)
            .ToListAsync(cancellationToken);
        var groups = summaryRows
            .Select(row => new ExpenseCategorySummaryDto(
                row.Id,
                row.Name,
                row.TotalAmount,
                row.ExpenseCount))
            .ToList();

        var total = groups.Sum(group => group.TotalAmount);
        var count = groups.Sum(group => group.ExpenseCount);
        var average = count == 0
            ? 0
            : decimal.Round(total / count, 2, MidpointRounding.AwayFromZero);

        return new ExpenseSummaryDto(rangeStart, rangeEnd, total, count, average, groups);
    }

    public static void ValidateListQuery(ExpenseListQuery query)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.Page < 1)
            errors[nameof(query.Page)] = ["Page must be greater than or equal to 1."];
        if (query.PageSize is < 1 or > 100)
            errors[nameof(query.PageSize)] = ["Page size must be between 1 and 100."];
        if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
            errors[nameof(query.From)] = ["From date cannot be after to date."];
        if (query.Search?.Trim().Length > 200)
            errors[nameof(query.Search)] = ["Search cannot exceed 200 characters."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    public static (DateOnly From, DateOnly To) ResolveSummaryRange(
        ExpenseSummaryQuery query,
        DateOnly today)
    {
        if (!query.From.HasValue && !query.To.HasValue)
            return (new DateOnly(today.Year, today.Month, 1), today);

        var errors = new Dictionary<string, string[]>();
        if (!query.From.HasValue)
            errors[nameof(query.From)] = ["From date is required when to date is provided."];
        if (!query.To.HasValue)
            errors[nameof(query.To)] = ["To date is required when from date is provided."];
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var from = query.From!.Value;
        var to = query.To!.Value;
        if (from > to)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(query.From)] = ["From date cannot be after to date."]
            });
        }

        return (from, to);
    }

    private async Task EnsureCategoryCanBeUsedAsync(
        Guid categoryId,
        Guid? currentCategoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(categoryId)] = ["Expense category is required."]
            });
        }

        var category = await _dbContext.ExpenseCategories
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == categoryId, cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseCategory), categoryId);
        if (!category.IsActive && currentCategoryId != categoryId)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(categoryId)] = ["Expense category must be active."]
            });
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(utc.Add(BusinessOffset));

    private static ExpenseDto ToDto(Expense expense, string categoryName, IReadOnlyList<ExpensePayment>? payments = null)
    {
        var ledgerPaid = payments?.Where(payment => payment.Status == PaymentTransactionStatus.Succeeded)
            .Sum(payment => payment.Amount) ?? 0;
        var paid = ledgerPaid > 0 ? ledgerPaid : expense.PaidAt.HasValue ? expense.Amount : 0;
        return new ExpenseDto(
            expense.Id, expense.ExpenseCategoryId, categoryName, expense.ExpenseDate, expense.Amount,
            expense.Description, expense.Notes, ToUtcOffset(expense.CreatedAt), ToUtcOffset(expense.UpdatedAt),
            expense.StockReceiptId, ToUtcOffset(expense.PaidAt), expense.PaymentMethod, expense.Vendor,
            expense.PaymentHistoryIncomplete, paid, Math.Max(0, expense.Amount - paid),
            payments?.Select(payment => new ExpensePaymentDto(payment.Id, payment.Amount, ToUtcOffset(payment.PaidAt),
                payment.Method, payment.Status, payment.Note)).ToList() ?? []);
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToUtcOffset(DateTime? value) =>
        value.HasValue ? ToUtcOffset(value.Value) : null;
}
