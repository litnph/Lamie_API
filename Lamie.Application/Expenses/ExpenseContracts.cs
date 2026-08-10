namespace Lamie.Application.Expenses;

public sealed record ExpenseCategoryDto(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    int ExpenseCount,
    decimal TotalAmount);

public sealed record CreateExpenseCategoryRequest(
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive);

public sealed record UpdateExpenseCategoryRequest(
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive);

public sealed record ExpenseDto(
    Guid Id,
    Guid ExpenseCategoryId,
    string ExpenseCategoryName,
    DateOnly ExpenseDate,
    decimal Amount,
    string Description,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateExpenseRequest(
    Guid ExpenseCategoryId,
    DateOnly ExpenseDate,
    decimal Amount,
    string Description,
    string? Notes);

public sealed record UpdateExpenseRequest(
    Guid ExpenseCategoryId,
    DateOnly ExpenseDate,
    decimal Amount,
    string Description,
    string? Notes);

public sealed class ExpenseListQuery
{
    public string? Search { get; init; }
    public Guid? ExpenseCategoryId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class ExpenseSummaryQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record PagedExpensesDto(
    IReadOnlyList<ExpenseDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record ExpenseCategorySummaryDto(
    Guid ExpenseCategoryId,
    string ExpenseCategoryName,
    decimal TotalAmount,
    int ExpenseCount);

public sealed record ExpenseSummaryDto(
    DateOnly From,
    DateOnly To,
    decimal TotalAmount,
    int ExpenseCount,
    decimal AverageAmount,
    IReadOnlyList<ExpenseCategorySummaryDto> ByCategory);

public interface IExpenseCategoryService
{
    Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<ExpenseCategoryDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(CreateExpenseCategoryRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(Guid id, UpdateExpenseCategoryRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IExpenseService
{
    Task<PagedExpensesDto> ListAsync(ExpenseListQuery query, CancellationToken cancellationToken);
    Task<ExpenseDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(CreateExpenseRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(Guid id, UpdateExpenseRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<ExpenseSummaryDto> GetSummaryAsync(ExpenseSummaryQuery query, CancellationToken cancellationToken);
}
