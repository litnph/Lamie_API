using Lamie.Application.Common.Exceptions;
using Lamie.Application.Expenses;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class ExpenseCategoryService : IExpenseCategoryService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ExpenseCategoryService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var categories = _dbContext.ExpenseCategories.AsNoTracking();
        if (!includeInactive)
            categories = categories.Where(category => category.IsActive);

        return await categories
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new ExpenseCategoryDto(
                category.Id,
                category.Name,
                category.Description,
                category.SortOrder,
                category.IsActive,
                _dbContext.Expenses.Count(expense => expense.ExpenseCategoryId == category.Id),
                _dbContext.Expenses
                    .Where(expense => expense.ExpenseCategoryId == category.Id)
                    .Sum(expense => (decimal?)expense.Amount) ?? 0))
            .ToListAsync(cancellationToken);
    }

    public async Task<ExpenseCategoryDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.ExpenseCategories
            .AsNoTracking()
            .Where(category => category.Id == id)
            .Select(category => new ExpenseCategoryDto(
                category.Id,
                category.Name,
                category.Description,
                category.SortOrder,
                category.IsActive,
                _dbContext.Expenses.Count(expense => expense.ExpenseCategoryId == category.Id),
                _dbContext.Expenses
                    .Where(expense => expense.ExpenseCategoryId == category.Id)
                    .Sum(expense => (decimal?)expense.Amount) ?? 0))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseCategory), id);
    }

    public async Task<Guid> CreateAsync(
        CreateExpenseCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedName = ExpenseCategory.NormalizeName(request.Name);
        if (await _dbContext.ExpenseCategories.AnyAsync(
                category => category.NormalizedName == normalizedName,
                cancellationToken))
        {
            throw new ConflictException($"Expense category '{request.Name.Trim()}' already exists.");
        }

        var category = new ExpenseCategory(
            request.Name,
            request.Description,
            request.SortOrder,
            request.IsActive,
            UtcNow());
        _dbContext.ExpenseCategories.Add(category);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException($"Expense category '{request.Name.Trim()}' already exists.");
        }

        return category.Id;
    }

    public async Task UpdateAsync(
        Guid id,
        UpdateExpenseCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _dbContext.ExpenseCategories.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseCategory), id);
        var normalizedName = ExpenseCategory.NormalizeName(request.Name);
        if (await _dbContext.ExpenseCategories.AnyAsync(
                item => item.Id != id && item.NormalizedName == normalizedName,
                cancellationToken))
        {
            throw new ConflictException($"Expense category '{request.Name.Trim()}' already exists.");
        }

        category.Update(
            request.Name,
            request.Description,
            request.SortOrder,
            request.IsActive,
            UtcNow());

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException($"Expense category '{request.Name.Trim()}' already exists.");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await _dbContext.ExpenseCategories.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(ExpenseCategory), id);

        if (await _dbContext.Expenses.AnyAsync(
                expense => expense.ExpenseCategoryId == id,
                cancellationToken))
        {
            throw new ConflictException(
                "Expense category cannot be deleted while it is used by expenses. Deactivate it instead.");
        }

        _dbContext.ExpenseCategories.Remove(category);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
