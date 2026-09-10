using Lamie.Application.Common.Exceptions;
using Lamie.Application.Expenses;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class ExpenseService : IExpenseService
{
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ExpenseService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
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

        return new PagedExpensesDto(
            rows.Select(row => ToDto(row.Expense, row.CategoryName)).ToList(),
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

        return ToDto(row.Expense, row.CategoryName);
    }

    public async Task<Guid> CreateAsync(
        CreateExpenseRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureCategoryCanBeUsedAsync(request.ExpenseCategoryId, null, cancellationToken);
        var expense = new Expense(
            request.ExpenseCategoryId,
            request.ExpenseDate,
            request.Amount,
            request.Description,
            request.Notes,
            UtcNow());
        _dbContext.Expenses.Add(expense);
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

        expense.Update(
            request.ExpenseCategoryId,
            request.ExpenseDate,
            request.Amount,
            request.Description,
            request.Notes,
            UtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var expense = await _dbContext.Expenses.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(Expense), id);
        if (expense.StockReceiptId.HasValue)
            throw new ConflictException("Chi phí được tạo từ phiếu nhập kho không thể xóa riêng. Hãy giữ chứng từ để bảo toàn lịch sử đối soát.");
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

    private static ExpenseDto ToDto(Expense expense, string categoryName) => new(
        expense.Id,
        expense.ExpenseCategoryId,
        categoryName,
        expense.ExpenseDate,
        expense.Amount,
        expense.Description,
        expense.Notes,
        ToUtcOffset(expense.CreatedAt),
        ToUtcOffset(expense.UpdatedAt),
        expense.StockReceiptId);

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
