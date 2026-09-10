using System.Data;
using System.Security.Claims;
using Lamie.API.Models.Inventory;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Inventory;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public InventoryService(
        AppDbContext dbContext,
        IFileStorage fileStorage,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public async Task<PagedInventoryStockUnitsDto> ListStockUnitsAsync(
        InventoryListQuery query,
        CancellationToken cancellationToken)
    {
        ValidatePage(query.Page, query.PageSize);
        if (query.Status.HasValue && !Enum.IsDefined(query.Status.Value))
            throw Validation(nameof(query.Status), "Stock status is invalid.");

        var rows =
            from stockUnit in _dbContext.InventoryStockUnits.AsNoTracking()
            join item in _dbContext.InventoryItems.AsNoTracking() on stockUnit.InventoryItemId equals item.Id
            join unit in _dbContext.MeasurementUnits.AsNoTracking() on item.MeasurementUnitId equals unit.Id
            select new { StockUnit = stockUnit, Item = item, Unit = unit };

        if (!query.IncludeInactive)
            rows = rows.Where(row => row.StockUnit.IsActive && row.Item.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(row =>
                row.Item.Code.Contains(search) ||
                row.Item.Name.Contains(search) ||
                (row.StockUnit.SizeName != null && row.StockUnit.SizeName.Contains(search)));
        }
        rows = query.Status switch
        {
            InventoryStockStatus.OutOfStock => rows.Where(row => row.StockUnit.Quantity <= 0),
            InventoryStockStatus.Low => rows.Where(row => row.StockUnit.Quantity > 0 && row.StockUnit.Quantity <= row.StockUnit.LowStockThreshold),
            InventoryStockStatus.Normal => rows.Where(row => row.StockUnit.Quantity > row.StockUnit.LowStockThreshold),
            _ => rows
        };

        var totalCount = await rows.CountAsync(cancellationToken);
        var entities = await rows
            .OrderBy(row => row.Item.Name)
            .ThenBy(row => row.StockUnit.SizeName)
            .ThenBy(row => row.StockUnit.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return new PagedInventoryStockUnitsDto(
            entities.Select(row => MapStockUnit(row.StockUnit, row.Item, row.Unit)).ToList(),
            totalCount,
            query.Page,
            query.PageSize,
            totalPages,
            query.Page < totalPages,
            query.Page > 1);
    }

    public async Task<PagedReplenishmentTasksDto> ListReplenishmentTasksAsync(
        InventoryListQuery query,
        CancellationToken cancellationToken)
    {
        ValidatePage(query.Page, query.PageSize);

        var warningRows =
            from stockUnit in _dbContext.InventoryStockUnits.AsNoTracking()
            join item in _dbContext.InventoryItems.AsNoTracking() on stockUnit.InventoryItemId equals item.Id
            join unit in _dbContext.MeasurementUnits.AsNoTracking() on item.MeasurementUnitId equals unit.Id
            where stockUnit.IsActive
                && item.IsActive
                && stockUnit.Quantity <= stockUnit.LowStockThreshold
            select new { StockUnit = stockUnit, Item = item, Unit = unit };
        var pendingCount = await warningRows.CountAsync(cancellationToken);
        var rows = warningRows;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(row =>
                row.Item.Code.Contains(search)
                || row.Item.Name.Contains(search)
                || (row.StockUnit.SizeName != null && row.StockUnit.SizeName.Contains(search)));
        }

        var totalCount = await rows.CountAsync(cancellationToken);
        var entities = await rows
            .OrderBy(row => row.StockUnit.Quantity <= 0 ? 0 : 1)
            .ThenBy(row => row.Item.Name)
            .ThenBy(row => row.StockUnit.SizeName)
            .ThenBy(row => row.StockUnit.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return new PagedReplenishmentTasksDto(
            entities.Select(row => new ReplenishmentTaskDto(
                row.StockUnit.Id,
                row.StockUnit.Id,
                row.Item.Id,
                row.Item.Code,
                row.Item.Name,
                row.StockUnit.SizeName,
                row.Unit.Name,
                row.Unit.Symbol,
                row.StockUnit.Quantity,
                row.StockUnit.LowStockThreshold,
                row.StockUnit.Status,
                UtcOffset(row.StockUnit.UpdatedAt))).ToList(),
            totalCount,
            pendingCount,
            query.Page,
            query.PageSize,
            totalPages,
            query.Page < totalPages,
            query.Page > 1);
    }

    public async Task<IReadOnlyList<InventoryItemDto>> ListItemsAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.InventoryItems.AsNoTracking();
        if (!includeInactive)
            query = query.Where(item => item.IsActive);
        var items = await query.OrderBy(item => item.Name).ThenBy(item => item.Code).ToListAsync(cancellationToken);
        var itemIds = items.Select(item => item.Id).ToArray();
        var units = await (
                from stockUnit in _dbContext.InventoryStockUnits.AsNoTracking()
                join item in _dbContext.InventoryItems.AsNoTracking() on stockUnit.InventoryItemId equals item.Id
                join measurementUnit in _dbContext.MeasurementUnits.AsNoTracking() on item.MeasurementUnitId equals measurementUnit.Id
                where itemIds.Contains(item.Id)
                orderby stockUnit.SizeName, stockUnit.Id
                select new { StockUnit = stockUnit, Item = item, Unit = measurementUnit })
            .ToListAsync(cancellationToken);
        var unitsByItem = units.GroupBy(row => row.Item.Id).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<InventoryStockUnitDto>)group.Select(row => MapStockUnit(row.StockUnit, row.Item, row.Unit)).ToList());
        var measurementUnitIds = items.Select(item => item.MeasurementUnitId).Distinct().ToArray();
        var measurementUnits = await _dbContext.MeasurementUnits.AsNoTracking()
            .Where(unit => measurementUnitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
        return items.Select(item => MapItem(item, measurementUnits[item.MeasurementUnitId], unitsByItem.GetValueOrDefault(item.Id) ?? [])).ToList();
    }

    public async Task<InventoryItemDto> GetItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await _dbContext.InventoryItems.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(InventoryItem), id);
        var unit = await _dbContext.MeasurementUnits.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == item.MeasurementUnitId, cancellationToken);
        return MapItem(item, unit, await GetItemStockUnitsAsync(id, cancellationToken));
    }

    public async Task<InventoryItemDto> CreateItemAsync(
        SaveInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        ValidateStockUnitConfiguration(request.StockUnits);
        await ValidateInventoryItemRequestAsync(request, null, cancellationToken);
        var now = UtcNow();
        var item = new InventoryItem(request.Code, request.Name, request.MeasurementUnitId, request.Note, request.IsActive, now);
        _dbContext.InventoryItems.Add(item);
        foreach (var stockUnit in request.StockUnits)
            _dbContext.InventoryStockUnits.Add(new InventoryStockUnit(item.Id, stockUnit.SizeName, stockUnit.LowStockThreshold, stockUnit.IsActive, now));
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("An inventory item with the same code already exists.");
        }
        return await GetItemAsync(item.Id, cancellationToken);
    }

    public async Task<InventoryItemDto> UpdateItemAsync(
        Guid id,
        SaveInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var item = await _dbContext.InventoryItems.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(InventoryItem), id);
        ValidateStockUnitConfiguration(request.StockUnits);
        await ValidateInventoryItemRequestAsync(request, id, cancellationToken);
        if (item.MeasurementUnitId != request.MeasurementUnitId)
        {
            var stockUnitIds = await _dbContext.InventoryStockUnits.AsNoTracking()
                .Where(unit => unit.InventoryItemId == id)
                .Select(unit => unit.Id)
                .ToArrayAsync(cancellationToken);
            var hasUsage = await _dbContext.StockTransactions.AsNoTracking().AnyAsync(transaction => stockUnitIds.Contains(transaction.StockUnitId), cancellationToken)
                || await _dbContext.OrderMaterials.AsNoTracking().AnyAsync(material => stockUnitIds.Contains(material.StockUnitId), cancellationToken)
                || await _dbContext.InventoryStockUnits.AsNoTracking().AnyAsync(unit => unit.InventoryItemId == id && unit.Quantity != 0, cancellationToken);
            if (hasUsage)
                throw new ConflictException("Cannot change the measurement unit after stock or transaction history exists.");
        }
        item.Update(request.Code, request.Name, request.MeasurementUnitId, request.Note, request.IsActive, UtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
        await SaveItemStockUnitsAsync(id, new SaveIngredientStockUnitsRequest(request.StockUnits), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetItemAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryStockUnitDto>> GetIngredientStockUnitsAsync(
        int ingredientId,
        CancellationToken cancellationToken)
    {
        var itemId = await _dbContext.InventoryItems.AsNoTracking()
            .Where(item => item.LegacyIngredientId == ingredientId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!itemId.HasValue)
            throw new NotFoundException(nameof(InventoryItem), ingredientId);
        return await GetItemStockUnitsAsync(itemId.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryStockUnitDto>> SaveIngredientStockUnitsAsync(
        int ingredientId,
        SaveIngredientStockUnitsRequest request,
        CancellationToken cancellationToken)
    {
        var item = await _dbContext.InventoryItems
            .SingleOrDefaultAsync(candidate => candidate.LegacyIngredientId == ingredientId, cancellationToken);
        if (item is null)
        {
            var ingredient = await _dbContext.Ingredients.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == ingredientId, cancellationToken)
                ?? throw new NotFoundException(nameof(Ingredient), ingredientId);
            item = new InventoryItem(ingredient.Code, ingredient.Name, ingredient.BaseUnitId, ingredient.Note,
                ingredient.IsActive, UtcNow(), ingredient.Id);
            _dbContext.InventoryItems.Add(item);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return await SaveItemStockUnitsAsync(item.Id, request, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryStockUnitDto>> GetItemStockUnitsAsync(
        Guid inventoryItemId,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from stockUnit in _dbContext.InventoryStockUnits.AsNoTracking()
                join item in _dbContext.InventoryItems.AsNoTracking() on stockUnit.InventoryItemId equals item.Id
                join unit in _dbContext.MeasurementUnits.AsNoTracking() on item.MeasurementUnitId equals unit.Id
                where stockUnit.InventoryItemId == inventoryItemId
                orderby stockUnit.SizeName, stockUnit.Id
                select new { StockUnit = stockUnit, Item = item, Unit = unit })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0 && !await _dbContext.InventoryItems.AnyAsync(item => item.Id == inventoryItemId, cancellationToken))
            throw new NotFoundException(nameof(InventoryItem), inventoryItemId);
        return rows.Select(row => MapStockUnit(row.StockUnit, row.Item, row.Unit)).ToList();
    }

    public async Task<IReadOnlyList<InventoryStockUnitDto>> SaveItemStockUnitsAsync(
        Guid inventoryItemId,
        SaveIngredientStockUnitsRequest request,
        CancellationToken cancellationToken)
    {
        ValidateStockUnitConfiguration(request.Items);
        var inventoryItem = await _dbContext.InventoryItems.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == inventoryItemId, cancellationToken)
            ?? throw new NotFoundException(nameof(InventoryItem), inventoryItemId);
        var existing = await _dbContext.InventoryStockUnits
            .Where(item => item.InventoryItemId == inventoryItemId)
            .ToListAsync(cancellationToken);
        var existingById = existing.ToDictionary(item => item.Id);
        var requestedIds = request.Items.Where(item => item.Id.HasValue).Select(item => item.Id!.Value).ToHashSet();
        if (requestedIds.Count != request.Items.Count(item => item.Id.HasValue) || requestedIds.Any(id => !existingById.ContainsKey(id)))
            throw Validation(nameof(request.Items), "A stock unit id does not belong to this material.");

        var omitted = existing.Where(item => !requestedIds.Contains(item.Id)).ToList();
        if (omitted.Count > 0)
        {
            var omittedIds = omitted.Select(item => item.Id).ToArray();
            var referenced = await _dbContext.StockTransactions.AsNoTracking().AnyAsync(item => omittedIds.Contains(item.StockUnitId), cancellationToken)
                || await _dbContext.OrderMaterials.AsNoTracking().AnyAsync(item => omittedIds.Contains(item.StockUnitId), cancellationToken);
            if (referenced || omitted.Any(item => item.Quantity != 0))
                throw new ConflictException("Stock units with quantity or history cannot be removed. Deactivate them instead.");
            _dbContext.InventoryStockUnits.RemoveRange(omitted);
        }

        var renamed = request.Items
            .Where(item => item.Id.HasValue && NormalizeSizeKey(existingById[item.Id.Value].SizeName) != NormalizeSizeKey(item.SizeName))
            .Select(item => item.Id!.Value)
            .ToArray();
        if (renamed.Length > 0)
        {
            var hasHistory = await _dbContext.StockTransactions.AsNoTracking()
                    .AnyAsync(item => renamed.Contains(item.StockUnitId), cancellationToken)
                || await _dbContext.OrderMaterials.AsNoTracking()
                    .AnyAsync(item => renamed.Contains(item.StockUnitId), cancellationToken)
                || renamed.Any(id => existingById[id].Quantity != 0);
            if (hasHistory)
                throw new ConflictException("Cannot change size configuration after stock or transaction history exists. Deactivate the old size and add a new one.");
        }

        var now = UtcNow();
        foreach (var item in request.Items)
        {
            if (item.Id.HasValue)
                existingById[item.Id.Value].UpdateSettings(item.SizeName, item.LowStockThreshold, item.IsActive, now);
            else
                _dbContext.InventoryStockUnits.Add(new InventoryStockUnit(inventoryItemId, item.SizeName, item.LowStockThreshold, item.IsActive, now));
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Stock configuration changed concurrently. Reload and try again.");
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("A size with the same name already exists for this material.");
        }

        return await GetItemStockUnitsAsync(inventoryItem.Id, cancellationToken);
    }

    public async Task<PagedStockTransactionsDto> ListTransactionsAsync(
        Guid stockUnitId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        if (!await _dbContext.InventoryStockUnits.AsNoTracking().AnyAsync(item => item.Id == stockUnitId, cancellationToken))
            throw new NotFoundException(nameof(InventoryStockUnit), stockUnitId);

        var query = _dbContext.StockTransactions.AsNoTracking().Where(item => item.StockUnitId == stockUnitId);
        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var receiptIds = transactions.Select(item => item.ReceiptId).OfType<Guid>().Distinct().ToArray();
        var orderIds = transactions.Select(item => item.OrderId).OfType<Guid>().Distinct().ToArray();
        var receipts = await _dbContext.StockReceipts.AsNoTracking()
            .Where(item => receiptIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.ReceiptNumber, cancellationToken);
        var orders = await _dbContext.Orders.AsNoTracking()
            .Where(item => orderIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.OrderCode, cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedStockTransactionsDto(
            transactions.Select(item => new StockTransactionDto(
                item.Id,
                item.StockUnitId,
                item.Type,
                item.QuantityDelta,
                item.BalanceAfter,
                item.ReceiptId,
                item.ReceiptId.HasValue ? receipts.GetValueOrDefault(item.ReceiptId.Value) : null,
                item.OrderId,
                item.OrderId.HasValue ? orders.GetValueOrDefault(item.OrderId.Value) : null,
                item.Note,
                UtcOffset(item.CreatedAt))).ToList(),
            totalCount,
            page,
            pageSize,
            totalPages,
            page < totalPages,
            page > 1);
    }

    public Task<StockTransactionDto> CreateIssueAsync(
        Guid stockUnitId,
        CreateStockIssueRequest request,
        CancellationToken cancellationToken) =>
        CreateManualTransactionAsync(
            stockUnitId,
            request.ClientRequestId,
            request.Quantity,
            request.Note,
            request.RowVersion,
            isAdjustment: false,
            cancellationToken);

    public Task<StockTransactionDto> CreateAdjustmentAsync(
        Guid stockUnitId,
        CreateStockAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        CreateManualTransactionAsync(
            stockUnitId,
            request.ClientRequestId,
            request.CountedQuantity,
            request.Note,
            request.RowVersion,
            isAdjustment: true,
            cancellationToken);

    private async Task<StockTransactionDto> CreateManualTransactionAsync(
        Guid stockUnitId,
        string clientRequestId,
        decimal requestedQuantity,
        string? note,
        string? rowVersion,
        bool isAdjustment,
        CancellationToken cancellationToken)
    {
        var requestId = clientRequestId?.Trim() ?? string.Empty;
        if (requestId.Length is < 1 or > 120)
            throw Validation(nameof(clientRequestId), "Client request id is required and cannot exceed 120 characters.");
        var operationKey = $"{(isAdjustment ? "adjustment" : "manual-issue")}:{stockUnitId:N}:{requestId}";
        var duplicate = await _dbContext.StockTransactions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationKey == operationKey, cancellationToken);
        if (duplicate is not null)
            return MapTransaction(duplicate);

        var stockUnit = await _dbContext.InventoryStockUnits
            .SingleOrDefaultAsync(item => item.Id == stockUnitId, cancellationToken)
            ?? throw new NotFoundException(nameof(InventoryStockUnit), stockUnitId);
        var inventoryItem = await _dbContext.InventoryItems.AsNoTracking()
            .SingleAsync(item => item.Id == stockUnit.InventoryItemId, cancellationToken);
        if (!stockUnit.IsActive || !inventoryItem.IsActive)
            throw new ConflictException("Inactive inventory items or stock units cannot be changed.");
        ApplyExpectedStockVersion(stockUnit, rowVersion);

        var normalized = NormalizeQuantity(requestedQuantity);
        var now = UtcNow();
        decimal delta;
        if (isAdjustment)
        {
            if (normalized < 0)
                throw Validation(nameof(requestedQuantity), "Counted inventory quantity cannot be negative.");
            delta = NormalizeQuantity(normalized - stockUnit.Quantity);
            if (delta == 0)
                throw Validation(nameof(requestedQuantity), "Counted quantity must differ from current stock.");
            stockUnit.AdjustTo(normalized, now);
        }
        else
        {
            if (normalized <= 0)
                throw Validation(nameof(requestedQuantity), "Issue quantity must be greater than zero.");
            if (normalized > stockUnit.Quantity)
                throw new ConflictException($"Insufficient inventory stock. Available: {stockUnit.Quantity}.");
            stockUnit.Remove(normalized, now);
            delta = -normalized;
        }

        var actor = CurrentActor();
        var transaction = new StockTransaction(
            stockUnit.Id,
            isAdjustment ? StockTransactionType.Adjustment : StockTransactionType.ManualIssue,
            delta,
            stockUnit.Quantity,
            operationKey,
            null,
            null,
            null,
            note,
            actor.Id,
            now);
        _dbContext.StockTransactions.Add(transaction);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Inventory changed concurrently. Reload and try again.");
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            duplicate = await _dbContext.StockTransactions.AsNoTracking()
                .SingleOrDefaultAsync(item => item.OperationKey == operationKey, CancellationToken.None);
            if (duplicate is not null)
                return MapTransaction(duplicate);
            throw new ConflictException("Inventory changed while the transaction was recorded. Reload and try again.");
        }
        return MapTransaction(transaction);
    }

    public async Task<StockReceiptDto> CreateReceiptAsync(
        CreateStockReceiptForm form,
        CancellationToken cancellationToken)
    {
        var requestId = form.ClientRequestId?.Trim() ?? string.Empty;
        if (requestId.Length is < 1 or > 120)
            throw Validation(nameof(form.ClientRequestId), "Client request id is required and cannot exceed 120 characters.");
        var existingId = await _dbContext.StockReceipts.AsNoTracking()
            .Where(item => item.ClientRequestId == requestId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingId.HasValue)
            return await GetReceiptAsync(existingId.Value, cancellationToken);
        if (form.ReceivedDate == default)
            throw Validation(nameof(form.ReceivedDate), "Receipt date is required.");
        if (form.Items.Count == 0)
            throw Validation(nameof(form.Items), "At least one receipt item is required.");
        if (form.Items.Count > 100)
            throw Validation(nameof(form.Items), "A receipt cannot contain more than 100 items.");
        if (form.Items.Any(item => item.StockUnitId == Guid.Empty || NormalizeQuantity(item.Quantity) <= 0))
            throw Validation(nameof(form.Items), "Every receipt item requires a stock unit and a positive quantity.");
        if (form.Items.Select(item => item.StockUnitId).Distinct().Count() != form.Items.Count)
            throw Validation(nameof(form.Items), "The same inventory item and size cannot appear twice in one receipt.");
        if (form.Invoice is not null && (!InvoiceUploadPolicy.HasAllowedMetadata(form.Invoice)
            || !await InvoiceUploadPolicy.HasValidSignatureAsync(form.Invoice, cancellationToken)))
            throw Validation(nameof(form.Invoice), "Invoice must be a valid JPG, PNG, WEBP, or PDF file up to 10 MB.");
        if (form.CreateExpense && (!form.ExpenseCategoryId.HasValue || form.ExpenseCategoryId == Guid.Empty || form.ExpenseAmount is null or <= 0))
            throw Validation(nameof(form.ExpenseCategoryId), "Expense category and a positive amount are required when recording an expense.");
        if (!form.CreateExpense && (form.ExpenseCategoryId.HasValue || form.ExpenseAmount.HasValue))
            throw Validation(nameof(form.CreateExpense), "Enable expense recording before supplying expense details.");

        ExpenseCategory? expenseCategory = null;
        if (form.CreateExpense)
        {
            expenseCategory = await _dbContext.ExpenseCategories.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == form.ExpenseCategoryId, cancellationToken)
                ?? throw new NotFoundException(nameof(ExpenseCategory), form.ExpenseCategoryId!.Value);
            if (!expenseCategory.IsActive)
                throw new ConflictException("Expense category is inactive.");
        }

        var stockUnitIds = form.Items.Select(item => item.StockUnitId).ToArray();
        var stockUnits = await _dbContext.InventoryStockUnits
            .Where(item => stockUnitIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (stockUnits.Count != stockUnitIds.Length)
            throw new ConflictException("One or more inventory stock units no longer exist.");
        if (stockUnits.Values.Any(item => !item.IsActive))
            throw new ConflictException("Inactive stock units cannot be used in a new receipt.");
        var receiptInventoryItemIds = stockUnits.Values.Select(unit => unit.InventoryItemId).Distinct().ToArray();
        var activeItemIds = await _dbContext.InventoryItems.AsNoTracking()
            .Where(item => item.IsActive && receiptInventoryItemIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (activeItemIds.Count != receiptInventoryItemIds.Length)
            throw new ConflictException("Inactive inventory items cannot be used in a new receipt.");

        var actor = CurrentActor();
        var now = UtcNow();
        var receipt = new StockReceipt(
            $"NK-{now:yyMMdd-HHmmss}-{Guid.NewGuid():N}"[..26].ToUpperInvariant(),
            requestId,
            form.ReceivedDate,
            form.Note,
            null,
            null,
            null,
            actor.Id,
            now);
        string? uploadedUrl = null;
        if (form.Invoice is not null)
        {
            var extension = Path.GetExtension(Path.GetFileName(form.Invoice.FileName)).ToLowerInvariant();
            await using var stream = form.Invoice.OpenReadStream();
            uploadedUrl = await _fileStorage.UploadPublicAsync(
                stream,
                $"inventory/receipts/{receipt.Id:N}/invoice-{Guid.NewGuid():N}{extension}",
                form.Invoice.ContentType,
                cancellationToken);
            receipt.AttachInvoice(
                uploadedUrl,
                Path.GetFileName(form.Invoice.FileName),
                form.Invoice.ContentType);
        }

        var committed = false;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.StockReceipts.Add(receipt);
            foreach (var formItem in form.Items)
            {
                var stockUnit = stockUnits[formItem.StockUnitId];
                var quantity = NormalizeQuantity(formItem.Quantity);
                var balance = stockUnit.Add(quantity, now);
                _dbContext.StockReceiptItems.Add(new StockReceiptItem(receipt.Id, stockUnit.Id, quantity));
                _dbContext.StockTransactions.Add(new StockTransaction(
                    stockUnit.Id,
                    StockTransactionType.Receipt,
                    quantity,
                    balance,
                    $"receipt:{receipt.Id:N}:stock-unit:{stockUnit.Id:N}",
                    receipt.Id,
                    null,
                    null,
                    form.Note,
                    actor.Id,
                    now));
            }
            if (form.CreateExpense)
            {
                _dbContext.Expenses.Add(new Expense(
                    expenseCategory!.Id,
                    form.ReceivedDate,
                    form.ExpenseAmount!.Value,
                    $"Nhập kho {receipt.ReceiptNumber}",
                    form.Note,
                    now,
                    receipt.Id));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException("Inventory changed concurrently. Reload stock and retry the receipt.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            var duplicateId = await _dbContext.StockReceipts.AsNoTracking()
                .Where(item => item.ClientRequestId == requestId)
                .Select(item => (Guid?)item.Id)
                .SingleOrDefaultAsync(CancellationToken.None);
            if (duplicateId.HasValue)
            {
                if (uploadedUrl is not null)
                    await DeleteFileBestEffortAsync(uploadedUrl);
                return await GetReceiptAsync(duplicateId.Value, CancellationToken.None);
            }
            throw new ConflictException("The receipt could not be recorded because inventory changed. Reload and retry.");
        }
        finally
        {
            if (!committed && uploadedUrl is not null)
                await DeleteFileBestEffortAsync(uploadedUrl);
        }

        return await GetReceiptAsync(receipt.Id, cancellationToken);
    }

    public async Task<StockReceiptDto> GetReceiptAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await _dbContext.StockReceipts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(StockReceipt), id);
        var itemRows = await (
                from receiptItem in _dbContext.StockReceiptItems.AsNoTracking()
                join stockUnit in _dbContext.InventoryStockUnits.AsNoTracking() on receiptItem.StockUnitId equals stockUnit.Id
                join inventoryItem in _dbContext.InventoryItems.AsNoTracking() on stockUnit.InventoryItemId equals inventoryItem.Id
                join unit in _dbContext.MeasurementUnits.AsNoTracking() on inventoryItem.MeasurementUnitId equals unit.Id
                where receiptItem.ReceiptId == id
                orderby inventoryItem.Name, stockUnit.SizeName
                select new { ReceiptItem = receiptItem, StockUnit = stockUnit, InventoryItem = inventoryItem, Unit = unit })
            .ToListAsync(cancellationToken);
        var expenseId = await _dbContext.Expenses.AsNoTracking()
            .Where(item => item.StockReceiptId == id)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return new StockReceiptDto(
            receipt.Id,
            receipt.ReceiptNumber,
            receipt.ClientRequestId,
            receipt.ReceivedDate,
            receipt.Note,
            receipt.InvoiceUrl,
            receipt.InvoiceFileName,
            expenseId,
            itemRows.Select(row => new StockReceiptItemDto(
                row.ReceiptItem.Id,
                row.StockUnit.Id,
                row.InventoryItem.Id,
                row.InventoryItem.Code,
                row.InventoryItem.Name,
                row.StockUnit.SizeName,
                row.Unit.Name,
                row.Unit.Symbol,
                row.ReceiptItem.Quantity)).ToList(),
            UtcOffset(receipt.CreatedAt));
    }

    public async Task<IReadOnlyList<OrderMaterialDto>> SaveOrderMaterialsAsync(
        Guid orderId,
        SaveOrderMaterialsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count > 100)
            throw Validation(nameof(request.Items), "An order cannot contain more than 100 inventory usage lines.");
        if (request.Items.Any(item => item.StockUnitId == Guid.Empty || NormalizeQuantity(item.Quantity) <= 0))
            throw Validation(nameof(request.Items), "Every inventory usage line requires a stock unit and a positive quantity.");
        if (request.Items.Select(item => item.StockUnitId).Distinct().Count() != request.Items.Count)
            throw Validation(nameof(request.Items), "The same inventory item and size cannot appear twice in an order.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var order = await _dbContext.Orders.SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new NotFoundException(nameof(Order), orderId);
        if (order.OrderStatus is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new ConflictException("Inventory usages cannot be changed after an order is completed or cancelled.");
        ApplyExpectedOrderVersion(order, request.OrderRowVersion);

        var existing = await _dbContext.OrderMaterials.Where(item => item.OrderId == orderId).ToListAsync(cancellationToken);
        var existingById = existing.ToDictionary(item => item.Id);
        var existingByStockUnit = existing.ToDictionary(item => item.StockUnitId);
        var suppliedIds = request.Items.Where(item => item.Id.HasValue).Select(item => item.Id!.Value).ToArray();
        if (suppliedIds.Distinct().Count() != suppliedIds.Length || suppliedIds.Any(id => !existingById.ContainsKey(id)))
            throw Validation(nameof(request.Items), "An order material id does not belong to this order.");

        var stockUnitIds = request.Items.Select(item => item.StockUnitId)
            .Concat(existing.Select(item => item.StockUnitId))
            .Distinct()
            .ToArray();
        var stockUnits = await _dbContext.InventoryStockUnits
            .Where(item => stockUnitIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (request.Items.Any(item => !stockUnits.ContainsKey(item.StockUnitId)))
            throw new ConflictException("One or more selected material sizes no longer exist.");
        var inventoryItemIds = stockUnits.Values.Select(item => item.InventoryItemId).Distinct().ToArray();
        var inventoryItems = await (
                from inventoryItem in _dbContext.InventoryItems.AsNoTracking()
                join unit in _dbContext.MeasurementUnits.AsNoTracking() on inventoryItem.MeasurementUnitId equals unit.Id
                where inventoryItemIds.Contains(inventoryItem.Id)
                select new { InventoryItem = inventoryItem, Unit = unit })
            .ToDictionaryAsync(item => item.InventoryItem.Id, cancellationToken);
        var now = UtcNow();
        var actor = CurrentActor();
        var committedStatus = order.OrderStatus is OrderStatus.Producing or OrderStatus.Shipping;
        var retained = new HashSet<Guid>();

        foreach (var requestItem in request.Items)
        {
            OrderMaterial? material = null;
            if (requestItem.Id.HasValue)
            {
                material = existingById[requestItem.Id.Value];
                if (material.StockUnitId != requestItem.StockUnitId)
                    throw Validation(nameof(request.Items), "Change size by removing the old line and adding a new line.");
            }
            else if (existingByStockUnit.TryGetValue(requestItem.StockUnitId, out var sameStockUnit))
            {
                material = sameStockUnit;
            }

            var stockUnit = stockUnits[requestItem.StockUnitId];
            if (material is null)
            {
                if (!stockUnit.IsActive || !inventoryItems[stockUnit.InventoryItemId].InventoryItem.IsActive)
                    throw new ConflictException("Inactive inventory items or sizes cannot be added to an order.");
                var catalog = inventoryItems[stockUnit.InventoryItemId];
                material = new OrderMaterial(
                    orderId,
                    stockUnit,
                    catalog.InventoryItem.Code,
                    catalog.InventoryItem.Name,
                    catalog.Unit.Name,
                    catalog.Unit.Symbol,
                    requestItem.Quantity,
                    now);
                _dbContext.OrderMaterials.Add(material);
            }
            else
            {
                material.UpdateRequiredQuantity(requestItem.Quantity, now);
            }
            retained.Add(material.Id);
            if (committedStatus)
                ApplyOrderMaterialTarget(material, stockUnit, material.RequiredQuantity, order, actor.Id, now);
        }

        foreach (var removed in existing.Where(item => !retained.Contains(item.Id)))
        {
            ApplyOrderMaterialTarget(removed, stockUnits[removed.StockUnitId], 0, order, actor.Id, now);
            _dbContext.OrderMaterials.Remove(removed);
        }
        order.TouchMaterialConfiguration(now, actor.Id);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConflictException("Order materials or inventory changed concurrently. Reload and try again.");
        }

        return await GetOrderMaterialsAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderMaterialDto>> GetOrderMaterialsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await (
                from material in _dbContext.OrderMaterials.AsNoTracking()
                join stockUnit in _dbContext.InventoryStockUnits.AsNoTracking() on material.StockUnitId equals stockUnit.Id
                where material.OrderId == orderId
                orderby material.InventoryItemName, material.SizeName
                select new OrderMaterialDto(
                    material.Id,
                    material.StockUnitId,
                    material.InventoryItemId,
                    material.InventoryItemCode,
                    material.InventoryItemName,
                    material.SizeName,
                    material.UnitName,
                    material.UnitSymbol,
                    material.RequiredQuantity,
                    material.DeductedQuantity,
                    stockUnit.Quantity,
                    stockUnit.IsActive))
            .ToListAsync(cancellationToken);
    }

    private void ApplyOrderMaterialTarget(
        OrderMaterial material,
        InventoryStockUnit stockUnit,
        decimal targetQuantity,
        Order order,
        Guid? actorId,
        DateTime now)
    {
        var target = NormalizeQuantity(targetQuantity);
        var delta = NormalizeQuantity(target - material.DeductedQuantity);
        if (delta == 0)
            return;
        if (delta > 0)
        {
            if (stockUnit.Quantity < delta)
            {
                var size = string.IsNullOrWhiteSpace(material.SizeName) ? "không size" : $"size {material.SizeName}";
                throw new ConflictException($"Vật tư kho '{material.InventoryItemName}' ({size}) không đủ tồn kho: cần thêm {delta}, hiện còn {stockUnit.Quantity}.");
            }
            stockUnit.Remove(delta, now);
        }
        else
        {
            stockUnit.Add(-delta, now);
        }
        material.SetDeductedQuantity(target, now);
        _dbContext.StockTransactions.Add(new StockTransaction(
            stockUnit.Id,
            delta > 0 ? StockTransactionType.OrderUsage : StockTransactionType.OrderReturn,
            -delta,
            stockUnit.Quantity,
            $"order:{order.Id:N}:material:{material.Id:N}:{Guid.NewGuid():N}",
            null,
            order.Id,
            material.Id,
            delta > 0 ? "Sử dụng nguyên liệu cho đơn hàng." : "Hoàn nguyên liệu do điều chỉnh đơn hàng.",
            actorId,
            now));
    }

    private void ApplyExpectedOrderVersion(Order order, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length != 8)
                throw new FormatException();
            if (!bytes.SequenceEqual(order.RowVersion))
                throw new ConflictException("Order changed concurrently. Reload before editing materials.");
        }
        catch (FormatException)
        {
            throw Validation(nameof(encoded), "Order version is invalid. Reload and try again.");
        }
    }

    private void ApplyExpectedStockVersion(InventoryStockUnit stockUnit, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length != 8)
                throw new FormatException();
            _dbContext.Entry(stockUnit).Property(item => item.RowVersion).OriginalValue = bytes;
        }
        catch (FormatException)
        {
            throw Validation(nameof(encoded), "Inventory version is invalid. Reload and try again.");
        }
    }

    private static StockTransactionDto MapTransaction(StockTransaction transaction) => new(
        transaction.Id,
        transaction.StockUnitId,
        transaction.Type,
        transaction.QuantityDelta,
        transaction.BalanceAfter,
        transaction.ReceiptId,
        null,
        transaction.OrderId,
        null,
        transaction.Note,
        UtcOffset(transaction.CreatedAt));

    private static InventoryStockUnitDto MapStockUnit(
        InventoryStockUnit stockUnit,
        InventoryItem inventoryItem,
        MeasurementUnit unit) => new(
        stockUnit.Id,
        inventoryItem.Id,
        inventoryItem.Code,
        inventoryItem.Name,
        stockUnit.SizeName,
        unit.Name,
        unit.Symbol,
        stockUnit.Quantity,
        stockUnit.LowStockThreshold,
        stockUnit.Status,
        stockUnit.IsActive,
        UtcOffset(stockUnit.UpdatedAt),
        stockUnit.RowVersion.Length == 0 ? null : Convert.ToBase64String(stockUnit.RowVersion));

    private static InventoryItemDto MapItem(
        InventoryItem item,
        MeasurementUnit unit,
        IReadOnlyList<InventoryStockUnitDto> stockUnits) => new(
        item.Id,
        item.Code,
        item.Name,
        item.MeasurementUnitId,
        unit.Name,
        unit.Symbol,
        item.Note,
        item.IsActive,
        stockUnits.Any(stockUnit => !string.IsNullOrWhiteSpace(stockUnit.SizeName)),
        item.LegacyIngredientId,
        stockUnits,
        UtcOffset(item.UpdatedAt),
        item.RowVersion.Length == 0 ? null : Convert.ToBase64String(item.RowVersion));

    private async Task ValidateInventoryItemRequestAsync(
        SaveInventoryItemRequest request,
        Guid? existingId,
        CancellationToken cancellationToken)
    {
        if (!await _dbContext.MeasurementUnits.AsNoTracking()
                .AnyAsync(unit => unit.Id == request.MeasurementUnitId && unit.IsActive, cancellationToken))
            throw Validation(nameof(request.MeasurementUnitId), "An active measurement unit is required.");
        var normalizedCode = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (await _dbContext.InventoryItems.AsNoTracking()
                .AnyAsync(item => item.Code == normalizedCode && item.Id != existingId, cancellationToken))
            throw new ConflictException("An inventory item with the same code already exists.");
    }

    private static void ValidateStockUnitConfiguration(IReadOnlyList<SaveInventoryStockUnitItem> items)
    {
        if (items.Count == 0)
            throw Validation(nameof(items), "At least one stock unit is required.");
        if (items.Count > 100)
            throw Validation(nameof(items), "An inventory item cannot have more than 100 stock units.");
        var sizeKeys = items.Select(item => NormalizeSizeKey(item.SizeName)).ToList();
        if (sizeKeys.Distinct(StringComparer.Ordinal).Count() != sizeKeys.Count)
            throw Validation(nameof(items), "Sizes must be unique for an inventory item.");
        if (items.Count > 1 && sizeKeys.Any(string.IsNullOrEmpty))
            throw Validation(nameof(items), "An inventory item with multiple stock units must name every size.");
        if (items.Any(item => item.LowStockThreshold < 0))
            throw Validation(nameof(items), "Low-stock thresholds cannot be negative.");
    }

    private static string NormalizeSizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static decimal NormalizeQuantity(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static void ValidatePage(int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
            errors[nameof(page)] = ["Page must be greater than or equal to 1."];
        if (pageSize is < 1 or > 100)
            errors[nameof(pageSize)] = ["Page size must be between 1 and 100."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private (Guid? Id, string? Name) CurrentActor()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var subject = principal?.FindFirstValue("sub") ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return (Guid.TryParse(subject, out var id) ? id : null, principal?.Identity?.Name);
    }

    private async Task DeleteFileBestEffortAsync(string url)
    {
        try { await _fileStorage.DeleteAsync(url, CancellationToken.None); }
        catch { }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static DateTimeOffset UtcOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
