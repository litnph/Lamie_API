using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Lamie.API.Models.Orders;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Finance;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Lamie.Domain.Orders;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class OrderService : IOrderService
{
    private const int MaximumPageSize = 100;
    private const int MaximumBatchSize = 50;
    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();

    private readonly AppDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly IInventoryService? _inventoryService;
    private readonly IFinancePeriodGuard? _financePeriodGuard;

    public OrderService(
        AppDbContext dbContext,
        IFileStorage fileStorage,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        IInventoryService? inventoryService = null,
        IFinancePeriodGuard? financePeriodGuard = null)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _inventoryService = inventoryService;
        _financePeriodGuard = financePeriodGuard;
    }

    private IInventoryService RequireInventoryService() =>
        _inventoryService ?? throw new InvalidOperationException("Inventory service is not configured.");

    public async Task<PagedOrdersDto> ListAsync(OrderListQuery request, CancellationToken cancellationToken)
    {
        if (request.Page < 1)
            throw Validation(nameof(request.Page), "Page must be greater than or equal to 1.");
        if (request.PageSize is < 1 or > MaximumPageSize)
            throw Validation(nameof(request.PageSize), $"Page size must be between 1 and {MaximumPageSize}.");
        ValidateEnum(request.OrderStatus, nameof(request.OrderStatus));
        ValidateEnum(request.PaymentStatus, nameof(request.PaymentStatus));
        ValidateEnum<OrderSortBy>(request.SortBy, nameof(request.SortBy));
        ValidateEnum<SortDirection>(request.SortDirection, nameof(request.SortDirection));
        ValidateDateRange(request.CreatedFrom, request.CreatedTo, nameof(request.CreatedFrom));
        ValidateDateRange(request.DeliveryFrom, request.DeliveryTo, nameof(request.DeliveryFrom));
        ValidateDateRange(request.CompletedFrom, request.CompletedTo, nameof(request.CompletedFrom));

        IQueryable<Order> query = _dbContext.Orders.AsNoTracking();
        if (request.OrderStatus.HasValue)
            query = query.Where(order => order.OrderStatus == request.OrderStatus.Value);
        else if (string.Equals(request.StatusGroup, "incomplete", StringComparison.OrdinalIgnoreCase))
            query = query.Where(order => order.OrderStatus == OrderStatus.Created
                || order.OrderStatus == OrderStatus.Confirmed
                || order.OrderStatus == OrderStatus.Producing
                || order.OrderStatus == OrderStatus.Ready
                || order.OrderStatus == OrderStatus.Shipping
                || order.OrderStatus == OrderStatus.DeliveryFailed
                || order.OrderStatus == OrderStatus.Delivered);
        else if (!string.IsNullOrWhiteSpace(request.StatusGroup)
                 && !string.Equals(request.StatusGroup, "all", StringComparison.OrdinalIgnoreCase))
            throw Validation(nameof(request.StatusGroup), "Status group must be incomplete or all.");
        if (request.PaymentStatus.HasValue)
            query = query.Where(order => order.PaymentStatus == request.PaymentStatus.Value);
        if (request.ChannelId.HasValue)
            query = query.Where(order => order.ChannelId == request.ChannelId.Value);
        query = ApplyFulfillmentFilter(query, request.FulfillmentType);

        query = ApplyDateRange(query, request.CreatedFrom, request.CreatedTo, order => order.CreatedAt);
        query = ApplyDateRange(query, request.DeliveryFrom, request.DeliveryTo, order => order.DeliveryAt);
        query = ApplyDateRange(query, request.CompletedFrom, request.CompletedTo, order => order.CompletedAt ?? DateTime.MinValue);

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var phone = new string(request.Phone.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(phone))
            {
                query = query.Where(order =>
                    order.OrdererPhone.Replace(" ", "").Replace("-", "").Contains(phone) ||
                    (order.RecipientPhone != null && order.RecipientPhone.Replace(" ", "").Replace("-", "").Contains(phone)));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(order =>
                order.OrderCode.Contains(search) ||
                order.OrdererName.Contains(search) ||
                order.OrdererPhone.Contains(search) ||
                (order.RecipientName != null && order.RecipientName.Contains(search)) ||
                (order.RecipientPhone != null && order.RecipientPhone.Contains(search)) ||
                order.Items.Any(item => item.ProductName.Contains(search)) ||
                (order.DeliveryAddress != null && order.DeliveryAddress.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var prioritizedQuery = query.OrderBy(order =>
            order.OrderStatus == OrderStatus.Completed || order.OrderStatus == OrderStatus.Cancelled ? 1 : 0);
        var orderedQuery = (request.SortBy, request.SortDirection) switch
        {
            (OrderSortBy.DeliveryAt, SortDirection.Ascending) => prioritizedQuery.ThenBy(order => order.DeliveryAt).ThenBy(order => order.OrderCode),
            (OrderSortBy.DeliveryAt, SortDirection.Descending) => prioritizedQuery.ThenByDescending(order => order.DeliveryAt).ThenByDescending(order => order.OrderCode),
            (OrderSortBy.CreatedAt, SortDirection.Ascending) => prioritizedQuery.ThenBy(order => order.CreatedAt).ThenBy(order => order.OrderCode),
            (OrderSortBy.CreatedAt, SortDirection.Descending) => prioritizedQuery.ThenByDescending(order => order.CreatedAt).ThenByDescending(order => order.OrderCode),
            (OrderSortBy.TotalAmount, SortDirection.Ascending) => prioritizedQuery.ThenBy(order => order.TotalAmount).ThenBy(order => order.OrderCode),
            (OrderSortBy.TotalAmount, SortDirection.Descending) => prioritizedQuery.ThenByDescending(order => order.TotalAmount).ThenByDescending(order => order.OrderCode),
            _ => prioritizedQuery.ThenBy(order => order.DeliveryAt).ThenBy(order => order.OrderCode)
        };
        var orders = await orderedQuery
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(order => order.Items)
            .Include(order => order.Images)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        var paymentTotals = await PaymentTotalsAsync(orders.Select(order => order.Id), cancellationToken);
        var channelNames = await ChannelNamesAsync(orders.Select(order => order.ChannelId), cancellationToken);
        var nowUtc = UtcNow();

        return new PagedOrdersDto(
            orders.Select(order => ToListDto(
                order,
                paymentTotals.GetValueOrDefault(order.Id).Amount,
                paymentTotals.GetValueOrDefault(order.Id).Count,
                channelNames.GetValueOrDefault(order.ChannelId),
                nowUtc)).ToList(),
            totalCount,
            request.Page,
            request.PageSize,
            totalPages,
            request.Page < totalPages,
            request.Page > 1);
    }

    public async Task<OrderDeliveryBoardDto> DeliveryBoardAsync(
        OrderListQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1)
            throw Validation(nameof(request.Page), "Page must be greater than or equal to 1.");
        if (request.PageSize is < 1 or > MaximumPageSize)
            throw Validation(nameof(request.PageSize), $"Page size must be between 1 and {MaximumPageSize}.");
        ValidateEnum(request.OrderStatus, nameof(request.OrderStatus));
        ValidateEnum(request.PaymentStatus, nameof(request.PaymentStatus));
        ValidateDateRange(request.CreatedFrom, request.CreatedTo, nameof(request.CreatedFrom));
        ValidateDateRange(request.DeliveryFrom, request.DeliveryTo, nameof(request.DeliveryFrom));
        ValidateDateRange(request.CompletedFrom, request.CompletedTo, nameof(request.CompletedFrom));
        ValidateDateRange(request.CompletedFrom, request.CompletedTo, nameof(request.CompletedFrom));

        IQueryable<Order> baseQuery = _dbContext.Orders.AsNoTracking();
        if (request.OrderStatus.HasValue)
            baseQuery = baseQuery.Where(order => order.OrderStatus == request.OrderStatus.Value);
        else if (string.Equals(request.StatusGroup, "incomplete", StringComparison.OrdinalIgnoreCase))
            baseQuery = baseQuery.Where(order => order.OrderStatus == OrderStatus.Created
                || order.OrderStatus == OrderStatus.Confirmed
                || order.OrderStatus == OrderStatus.Producing
                || order.OrderStatus == OrderStatus.Ready
                || order.OrderStatus == OrderStatus.Shipping
                || order.OrderStatus == OrderStatus.DeliveryFailed
                || order.OrderStatus == OrderStatus.Delivered);
        else if (!string.IsNullOrWhiteSpace(request.StatusGroup)
                 && !string.Equals(request.StatusGroup, "all", StringComparison.OrdinalIgnoreCase))
            throw Validation(nameof(request.StatusGroup), "Status group must be incomplete or all.");

        if (request.PaymentStatus.HasValue)
            baseQuery = baseQuery.Where(order => order.PaymentStatus == request.PaymentStatus.Value);
        if (request.ChannelId.HasValue)
            baseQuery = baseQuery.Where(order => order.ChannelId == request.ChannelId.Value);
        baseQuery = ApplyFulfillmentFilter(baseQuery, request.FulfillmentType);
        baseQuery = ApplyDateRange(baseQuery, request.CreatedFrom, request.CreatedTo, order => order.CreatedAt);

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var phone = new string(request.Phone.Where(char.IsDigit).ToArray());
            if (phone.Length > 0)
                baseQuery = baseQuery.Where(order =>
                    order.OrdererPhone.Replace(" ", "").Replace("-", "").Contains(phone)
                    || (order.RecipientPhone != null && order.RecipientPhone.Replace(" ", "").Replace("-", "").Contains(phone)));
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            baseQuery = baseQuery.Where(order =>
                order.OrderCode.Contains(search)
                || order.OrdererName.Contains(search)
                || order.OrdererPhone.Contains(search)
                || (order.RecipientName != null && order.RecipientName.Contains(search))
                || (order.RecipientPhone != null && order.RecipientPhone.Contains(search))
                || order.Items.Any(item => item.ProductName.Contains(search))
                || (order.DeliveryAddress != null && order.DeliveryAddress.Contains(search)));
        }

        var now = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, BusinessTimeZone);
        var localToday = DateOnly.FromDateTime(localNow.DateTime);
        var todayStartUtc = BusinessStartUtc(localToday);
        var tomorrowStartUtc = BusinessStartUtc(localToday.AddDays(1));
        IQueryable<Order> rangeQuery;
        if (request.IncludeBacklog && request.DeliveryFrom.HasValue)
        {
            var from = request.DeliveryFrom.Value.UtcDateTime;
            var to = request.DeliveryTo?.UtcDateTime;
            rangeQuery = baseQuery.Where(order =>
                (order.DeliveryAt >= from && (!to.HasValue || order.DeliveryAt <= to.Value))
                || (order.DeliveryAt < from
                    && order.OrderStatus != OrderStatus.Completed
                    && order.OrderStatus != OrderStatus.Cancelled));
        }
        else
        {
            rangeQuery = ApplyDateRange(baseQuery, request.DeliveryFrom, request.DeliveryTo, order => order.DeliveryAt);
        }
        if (request.CompletedFrom.HasValue || request.CompletedTo.HasValue)
            rangeQuery = ApplyDateRange(rangeQuery, request.CompletedFrom, request.CompletedTo, order => order.CompletedAt ?? DateTime.MinValue);

        var totalCount = await rangeQuery.CountAsync(cancellationToken);
        var overdueCount = await rangeQuery.CountAsync(order => order.DeliveryAt < todayStartUtc
            && order.OrderStatus != OrderStatus.Completed
            && order.OrderStatus != OrderStatus.Cancelled
            && order.OrderStatus != OrderStatus.Delivered, cancellationToken);
        var orders = await rangeQuery
            .OrderBy(order =>
                order.OrderStatus != OrderStatus.Completed
                && order.OrderStatus != OrderStatus.Cancelled
                && order.OrderStatus != OrderStatus.Delivered
                && order.DeliveryAt < now.UtcDateTime
                    ? 0
                    : order.OrderStatus != OrderStatus.Completed
                      && order.OrderStatus != OrderStatus.Cancelled
                      && order.OrderStatus != OrderStatus.Delivered
                      && order.DeliveryAt >= now.UtcDateTime
                        ? 1
                        : order.OrderStatus != OrderStatus.Cancelled
                          && order.PaymentStatus != PaymentStatus.Paid
                            ? 2
                            : order.OrderStatus == OrderStatus.Completed
                                ? 3
                                : 4)
            .ThenBy(order => order.DeliveryAt)
            .ThenBy(order => order.OrderCode)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(order => order.Items)
            .Include(order => order.Images)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var totals = await PaymentTotalsAsync(orders.Select(order => order.Id), cancellationToken);
        var channelNames = await ChannelNamesAsync(orders.Select(order => order.ChannelId), cancellationToken);
        var items = orders.Select(order => ToListDto(
            order,
            totals.GetValueOrDefault(order.Id).Amount,
            totals.GetValueOrDefault(order.Id).Count,
            channelNames.GetValueOrDefault(order.ChannelId),
            now.UtcDateTime)).ToList();

        var totalAmount = await rangeQuery.SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0;
        var filteredIds = rangeQuery.Select(order => order.Id);
        var paidAmount = await _dbContext.OrderPayments.AsNoTracking()
            .Where(payment => filteredIds.Contains(payment.OrderId) && payment.Status == PaymentTransactionStatus.Succeeded)
            .SumAsync(payment => (decimal?)(payment.Kind == OrderPaymentKind.Refund ? -payment.Amount : payment.Amount), cancellationToken) ?? 0;
        var lateCount = await rangeQuery.CountAsync(order => order.DeliveryAt < now.UtcDateTime && order.OrderStatus != OrderStatus.Completed && order.OrderStatus != OrderStatus.Cancelled && order.OrderStatus != OrderStatus.Delivered, cancellationToken);
        var undeliveredTodayCount = await rangeQuery.CountAsync(order => order.DeliveryAt >= todayStartUtc && order.DeliveryAt < tomorrowStartUtc && order.OrderStatus != OrderStatus.Completed && order.OrderStatus != OrderStatus.Cancelled && order.OrderStatus != OrderStatus.Delivered, cancellationToken);
        var upcomingCount = await rangeQuery.CountAsync(order => order.DeliveryAt >= tomorrowStartUtc && order.OrderStatus != OrderStatus.Completed && order.OrderStatus != OrderStatus.Cancelled && order.OrderStatus != OrderStatus.Delivered, cancellationToken);
        var deliveredUnpaidCount = await rangeQuery.CountAsync(order => order.OrderStatus == OrderStatus.Delivered && order.PaymentStatus != PaymentStatus.Paid, cancellationToken);
        var completedCount = await rangeQuery.CountAsync(order => order.OrderStatus == OrderStatus.Completed, cancellationToken);
        var cancelledCount = await rangeQuery.CountAsync(order => order.OrderStatus == OrderStatus.Cancelled, cancellationToken);

        var summary = new OrderDeliveryBoardSummaryDto(
            totalCount,
            undeliveredTodayCount,
            lateCount,
            totalAmount,
            paidAmount,
            Math.Max(0, totalAmount - paidAmount),
            overdueCount)
        {
            LateCount = lateCount,
            UndeliveredTodayCount = undeliveredTodayCount,
            UpcomingCount = upcomingCount,
            DeliveredUnpaidCount = deliveredUnpaidCount,
            CompletedCount = completedCount,
            CancelledCount = cancelledCount
        };
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)request.PageSize);
        return new OrderDeliveryBoardDto(items, summary, now)
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = totalPages
        };
    }

    public async Task<OrderDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await GetAggregateAsync(id, false, cancellationToken);
        var payments = await _dbContext.OrderPayments.AsNoTracking()
            .Where(payment => payment.OrderId == id)
            .OrderByDescending(payment => payment.PaidAt)
            .ThenByDescending(payment => payment.Id)
            .ToListAsync(cancellationToken);
        var detail = ToDetailDto(order, payments);
        return _inventoryService is null
            ? detail
            : detail with { Materials = await _inventoryService.GetOrderMaterialsAsync(id, cancellationToken) };
    }

    public async Task<OrderDetailDto> CreateAsync(CreateOrderForm form, CancellationToken cancellationToken)
    {
        var uploadedUrls = new List<string>();
        var committed = false;
        IReadOnlyList<Lamie.Application.Inventory.OrderMaterialDto>? materials = null;
        try
        {
            var order = await BuildOrderAsync(form, uploadedUrls, cancellationToken);
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                form.MaterialsSpecified ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                cancellationToken);
            _dbContext.Orders.Add(order);
            AddInitialPayment(form, order);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (form.MaterialsSpecified)
                {
                    materials = await RequireInventoryService().SaveOrderMaterialsAsync(
                        order.Id,
                        new Lamie.Application.Inventory.SaveOrderMaterialsRequest(form.Materials, null),
                        cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                committed = true;
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("The order could not be created because its generated code conflicted. Please retry.");
            }

            return ToDetailDto(order) with { Materials = materials };
        }
        finally
        {
            if (!committed)
                await DeleteFilesBestEffortAsync(uploadedUrls);
        }
    }

    public async Task<BatchCreateOrdersDto> CreateBatchAsync(
        BatchCreateOrdersForm form,
        CancellationToken cancellationToken)
    {
        if (form.Orders.Count == 0)
            throw Validation("orders", "At least one order is required.");
        if (form.Orders.Count > MaximumBatchSize)
            throw Validation("orders", $"A batch cannot contain more than {MaximumBatchSize} orders.");

        var draftIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < form.Orders.Count; index++)
        {
            var clientDraftId = form.Orders[index].ClientDraftId?.Trim();
            if (string.IsNullOrWhiteSpace(clientDraftId))
                throw Validation($"orders[{index}].clientDraftId", "Client draft id is required.");
            if (clientDraftId.Length > 120)
                throw Validation($"orders[{index}].clientDraftId", "Client draft id cannot exceed 120 characters.");
            if (!draftIds.Add(clientDraftId))
                throw Validation($"orders[{index}].clientDraftId", "Client draft ids must be unique within a batch.");
        }

        var uploadedUrls = new List<string>();
        var createdOrders = new List<BatchCreatedOrderDto>(form.Orders.Count);
        var committed = false;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            form.Orders.Any(item => item.MaterialsSpecified) ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            cancellationToken);
        for (var index = 0; index < form.Orders.Count; index++)
        {
            var orderForm = form.Orders[index];
            var clientDraftId = orderForm.ClientDraftId!.Trim();
            try
            {
                var order = await BuildOrderAsync(orderForm, uploadedUrls, cancellationToken);
                _dbContext.Orders.Add(order);
                AddInitialPayment(orderForm, order);
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (orderForm.MaterialsSpecified)
                {
                    await RequireInventoryService().SaveOrderMaterialsAsync(
                        order.Id,
                        new Lamie.Application.Inventory.SaveOrderMaterialsRequest(orderForm.Materials, null),
                        cancellationToken);
                }
                createdOrders.Add(new BatchCreatedOrderDto(clientDraftId, order.Id, order.OrderCode));
            }
            catch (OperationCanceledException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await DeleteFilesBestEffortAsync(uploadedUrls);
                throw;
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await DeleteFilesBestEffortAsync(uploadedUrls);
                throw BatchOrderException.From(index, clientDraftId, exception);
            }
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new BatchCreateOrdersDto(createdOrders.Count, createdOrders);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var lastIndex = form.Orders.Count - 1;
            throw BatchOrderException.From(
                lastIndex,
                form.Orders[lastIndex].ClientDraftId!.Trim(),
                exception);
        }
        finally
        {
            if (!committed)
                await DeleteFilesBestEffortAsync(uploadedUrls);
        }
    }

    public async Task<OrderDetailDto> UpdateAsync(
        Guid id,
        UpdateOrderForm form,
        CancellationToken cancellationToken)
    {
        if (form.Id != Guid.Empty && form.Id != id)
            throw Validation(nameof(form.Id), "Route id and body id must match.");
        await EnsureActiveChannelAsync(form.ChannelId, cancellationToken);
        await using var transaction = form.MaterialsSpecified
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var actor = CurrentActor();
        var now = UtcNow();
        var order = await GetAggregateAsync(id, true, cancellationToken);
        await InvalidateOrderDateAsync(order, "Sửa đơn đã hoàn tất", cancellationToken);
        ApplyExpectedRowVersion(order, form.RowVersion);
        var snapshots = await ResolveSnapshotsAsync(
            form.Items,
            now,
            cancellationToken,
            order.Items.ToDictionary(item => item.Id));
        var trackedChildren = CaptureTrackedChildren(order);
        var customer = await ResolveCustomerAsync(
            form.CustomerId,
            form.CreateMember,
            form.OrdererName,
            form.OrdererPhone ?? string.Empty,
            now,
            cancellationToken);
        var address = await ResolveAddressSnapshotAsync(
            form.PickupAtShop,
            form.AddressScheme,
            form.ProvinceCode,
            form.DistrictCode,
            form.CommuneCode,
            form.AddressDetail,
            form.FullAddressSnapshot ?? form.DeliveryAddress,
            cancellationToken);

        var itemUpdates = form.Items.Select((item, index) =>
        {
            Guid? itemId = null;
            if (!string.IsNullOrWhiteSpace(item.Id))
            {
                if (!Guid.TryParse(item.Id, out var parsedId))
                    throw Validation(nameof(item.Id), "Order item id must be a valid GUID.");
                itemId = parsedId;
            }

            return new OrderItemUpdate(itemId, snapshots[index]);
        }).ToList();

        var removedUrls = order.Update(
            form.ChannelId,
            customer?.Id,
            ToDetails(form, address),
            itemUpdates,
            now,
            actor.Id,
            actor.Name,
            form.IsPaid);

        var currentItems = order.Items.ToList();

        var uploadedUrls = new List<string>();
        IReadOnlyList<Lamie.Application.Inventory.OrderMaterialDto>? materials = null;
        try
        {
            await ValidateImagesAsync(form.Images, currentItems.Count, cancellationToken);
            foreach (var image in form.Images.Where(item => item.ImageFile is { Length: > 0 }))
            {
                var file = image.ImageFile!;
                var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
                var objectPath = $"orders/{order.Id:N}/{image.OrderItemIndex:D3}-{image.SortOrder:D3}-{Guid.NewGuid():N}{extension}";
                await using var stream = file.OpenReadStream();
                var url = await _fileStorage.UploadPublicAsync(stream, objectPath, file.ContentType, cancellationToken);
                uploadedUrls.Add(url);
                order.AddImage(currentItems[image.OrderItemIndex].Id, url, image.SortOrder);
            }
            TrackAddedChildren(order, trackedChildren);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (form.MaterialsSpecified)
            {
                materials = await RequireInventoryService().SaveOrderMaterialsAsync(
                    order.Id,
                    new Lamie.Application.Inventory.SaveOrderMaterialsRequest(form.Materials, null),
                    cancellationToken);
            }
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            await DeleteFilesBestEffortAsync(uploadedUrls);
            throw new ConflictException("Đơn hàng đã được xử lý bởi người khác, vui lòng tải lại.");
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            await DeleteFilesBestEffortAsync(uploadedUrls);
            throw;
        }

        await DeleteFilesBestEffortAsync(removedUrls);

        return ToDetailDto(order) with { Materials = materials };
    }

    public async Task ChangeStatusAsync(Guid id, OrderStatus status, CancellationToken cancellationToken)
    {
        _ = await ChangeStatusCoreAsync(id, status, null, null, false, cancellationToken);
    }

    public Task<OrderDetailDto> OverrideStatusAsync(
        Guid id,
        OverrideOrderStatusRequest request,
        CancellationToken cancellationToken) =>
        ChangeStatusCoreAsync(id, request.Status, request.RowVersion, request.Reason, true, cancellationToken);

    private async Task<OrderDetailDto> ChangeStatusCoreAsync(
        Guid id,
        OrderStatus status,
        string? rowVersion,
        string? reason,
        bool overrideTransition,
        CancellationToken cancellationToken)
    {
        ValidateEnum<OrderStatus>(status, nameof(status));
        var actor = CurrentActor();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var order = await GetAggregateAsync(id, true, cancellationToken);
        ApplyExpectedRowVersion(order, rowVersion);
        if (order.OrderStatus == status)
        {
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(id, cancellationToken);
        }
        await InvalidateOrderDateAsync(order, "Đổi trạng thái đơn hoàn tất", cancellationToken);
        if (status == OrderStatus.Delivered && _financePeriodGuard is not null)
            await _financePeriodGuard.InvalidateAsync(LocalBusinessDate(UtcNow()), "Hoàn tất giao đơn", cancellationToken);
        var trackedChildren = CaptureTrackedChildren(order);
        var reserve = order.RequiresInventoryReservation(status);
        var restore = order.RequiresInventoryRestore(status);
        var inventoryReservedAfterTransition = order.InventoryReserved;

        if (reserve || restore)
        {
            var quantities = order.Items
                .Where(item => item.ProductId.HasValue)
                .GroupBy(item => item.ProductId!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
            var products = await _dbContext.Products
                .Where(product => quantities.Keys.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, cancellationToken);
            if (products.Count != quantities.Count)
                throw new ConflictException("Một hoặc nhiều sản phẩm trong đơn không còn tồn tại.");

            foreach (var (productId, quantity) in quantities)
            {
                var product = products[productId];
                if (reserve)
                {
                    if (!product.IsActive)
                        throw new ConflictException($"Sản phẩm '{product.Sku}' đã ngừng hoạt động.");
                    if (product.TracksInventory && product.Stock < quantity)
                        throw new ConflictException($"Sản phẩm '{product.Sku}' không đủ tồn kho: cần {quantity}, hiện còn {product.Stock}.");
                    product.ReserveStock(quantity);
                }
                else
                {
                    product.RestoreStock(quantity);
                }
            }

            inventoryReservedAfterTransition = reserve
                ? products.Values.Any(product => product.TracksInventory)
                : false;
        }

        var now = UtcNow();
        await ApplyMaterialLifecycleAsync(order, status, actor.Id, now, cancellationToken);
        if (overrideTransition)
            order.OverrideStatus(status, inventoryReservedAfterTransition, now, actor.Id, actor.Name, reason);
        else
            order.ChangeStatus(status, inventoryReservedAfterTransition, now, actor.Id, actor.Name, reason);
        TrackAddedChildren(order, trackedChildren);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("Đơn hàng hoặc tồn kho đã thay đổi bởi người khác, vui lòng tải lại.");
        }
        return await GetAsync(id, cancellationToken);
    }

    public async Task<OrderDetailDto> RunWorkflowActionAsync(
        Guid id,
        string action,
        OrderWorkflowCommand command,
        CancellationToken cancellationToken)
    {
        var normalized = action.Trim().ToLowerInvariant();
        var target = normalized switch
        {
            "confirm" => OrderStatus.Confirmed,
            "prepare-order" => OrderStatus.Producing,
            "start-preparation" => OrderStatus.Producing,
            "ready" => OrderStatus.Ready,
            "dispatch" => OrderStatus.Shipping,
            "delivered" => OrderStatus.Delivered,
            "delivery-failed" => OrderStatus.DeliveryFailed,
            "retry-delivery" => OrderStatus.Ready,
            "cancel" => OrderStatus.Cancelled,
            _ => throw Validation(nameof(action), "Thao tác quy trình không hợp lệ.")
        };
        var marker = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : $"[idempotency:{command.IdempotencyKey.Trim()}]";
        if (marker is not null && await _dbContext.OrderChangeLogs.AsNoTracking()
                .AnyAsync(log => log.OrderId == id && log.Note != null && log.Note.Contains(marker), cancellationToken))
            return await GetAsync(id, cancellationToken);
        var note = string.Join(Environment.NewLine, new[] { command.Reason?.Trim(), marker }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return await ChangeStatusCoreAsync(id, target, command.RowVersion, string.IsNullOrWhiteSpace(note) ? null : note, false, cancellationToken);
    }

    public Task<OrderDetailDto> UpdateRecipientAsync(Guid id, UpdateOrderRecipientRequest request, CancellationToken cancellationToken) =>
        UpdateAggregateAsync(id, request.RowVersion,
            (order, now, actorId, actorName) => order.UpdateRecipient(request.RecipientName, request.RecipientPhone, request.DeliveryAddress, now, actorId, actorName),
            cancellationToken);

    public Task<OrderDetailDto> UpdateAssignmentAsync(Guid id, UpdateOrderAssignmentRequest request, CancellationToken cancellationToken) =>
        UpdateAggregateAsync(id, request.RowVersion,
            (order, now, actorId, actorName) => order.UpdateAssignment(request.AssigneeId, request.AssigneeName, request.PreparationDate, now, actorId, actorName),
            cancellationToken);

    public Task<OrderDetailDto> UpdateChecklistAsync(Guid id, UpdateOrderChecklistRequest request, CancellationToken cancellationToken) =>
        UpdateAggregateAsync(id, request.RowVersion,
            (order, now, actorId, actorName) => order.UpdateChecklist(request.DesignConfirmed, request.MaterialsPrepared, request.CardPrepared, request.QualityChecked, now, actorId, actorName),
            cancellationToken);

    public Task<OrderDetailDto> HoldAsync(Guid id, HoldOrderRequest request, CancellationToken cancellationToken) =>
        UpdateAggregateAsync(id, request.RowVersion,
            (order, now, actorId, actorName) => order.Hold(request.Reason, now, actorId, actorName),
            cancellationToken);

    public Task<OrderDetailDto> ResumeAsync(Guid id, ResumeOrderRequest request, CancellationToken cancellationToken) =>
        UpdateAggregateAsync(id, request.RowVersion,
            (order, now, actorId, actorName) => order.Resume(now, actorId, actorName),
            cancellationToken);

    private async Task<OrderDetailDto> UpdateAggregateAsync(
        Guid id,
        string? rowVersion,
        Action<Order, DateTime, Guid?, string?> mutate,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        var order = await GetAggregateAsync(id, true, cancellationToken);
        ApplyExpectedRowVersion(order, rowVersion);
        var trackedChildren = CaptureTrackedChildren(order);
        mutate(order, UtcNow(), actor.Id, actor.Name);
        TrackAddedChildren(order, trackedChildren);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Đơn hàng đã được cập nhật ở phiên khác. Vui lòng tải lại trước khi thao tác tiếp.");
        }
        return await GetAsync(id, cancellationToken);
    }

    public async Task ReturnToPreviousStatusAsync(
        Guid id,
        ReturnOrderToPreviousStatusRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var order = await GetAggregateAsync(id, true, cancellationToken);
        ApplyExpectedRowVersion(order, request.RowVersion);
        await InvalidateOrderDateAsync(order, "Trả lại trạng thái đơn", cancellationToken);
        var target = order.PreviousStatus
            ?? throw new ConflictException("Trạng thái hiện tại không thể trả về bước xử lý trước đó.");
        var trackedChildren = CaptureTrackedChildren(order);

        if (order.OrderStatus == OrderStatus.Producing && order.InventoryReserved)
        {
            var quantities = order.Items
                .Where(item => item.ProductId.HasValue)
                .GroupBy(item => item.ProductId!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
            var products = await _dbContext.Products
                .Where(product => quantities.Keys.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, cancellationToken);
            if (products.Count != quantities.Count)
                throw new ConflictException("Một hoặc nhiều sản phẩm trong đơn không còn tồn tại.");

            foreach (var (productId, quantity) in quantities)
                products[productId].RestoreStock(quantity);
        }

        var now = UtcNow();
        await ApplyMaterialLifecycleAsync(order, target, actor.Id, now, cancellationToken);
        order.ReturnToPreviousStatus(false, now, actor.Id, actor.Name, "Trả đơn hàng về bước xử lý liền trước.");
        TrackAddedChildren(order, trackedChildren);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("Đơn hàng hoặc tồn kho đã thay đổi bởi người khác, vui lòng tải lại.");
        }
    }

    public async Task<IReadOnlyList<OrderPaymentDto>> ListPaymentsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await _dbContext.Orders.AsNoTracking().AnyAsync(order => order.Id == id, cancellationToken))
            throw new NotFoundException(nameof(Order), id);
        return await _dbContext.OrderPayments.AsNoTracking()
            .Where(payment => payment.OrderId == id)
            .OrderByDescending(payment => payment.PaidAt)
            .ThenByDescending(payment => payment.Id)
            .Select(payment => new OrderPaymentDto(
                payment.Id,
                payment.OrderId,
                payment.Amount,
                payment.Kind,
                payment.Method,
                UtcOffset(payment.PaidAt),
                payment.Status,
                payment.Note,
                payment.AffectsRecognizedRevenue,
                payment.ProductRevenueAdjustment,
                payment.ShippingRevenueAdjustment))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderPaymentDto> AddPaymentAsync(
        Guid id,
        CreateOrderPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateEnum<OrderPaymentKind>(request.Kind, nameof(request.Kind));
        ValidateEnum<PaymentMethod>(request.Method, nameof(request.Method));
        var existing = await _dbContext.OrderPayments.AsNoTracking()
            .SingleOrDefaultAsync(payment => payment.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.OrderId != id)
                throw new ConflictException("Mã xác nhận thanh toán đã được dùng cho giao dịch khác.");
            if (existing.Amount != request.Amount
                || existing.Kind != request.Kind
                || existing.Method != request.Method
                || existing.PaidAt != request.PaidAt.UtcDateTime
                || existing.AffectsRecognizedRevenue != request.AffectsRecognizedRevenue
                || existing.ProductRevenueAdjustment != request.ProductRevenueAdjustment
                || existing.ShippingRevenueAdjustment != request.ShippingRevenueAdjustment)
                throw new ConflictException("Mã xác nhận thanh toán đã tồn tại nhưng nội dung giao dịch không khớp.");
            return ToPaymentDto(existing);
        }

        var order = await GetAggregateAsync(id, true, cancellationToken);
        var trackedChildren = CaptureTrackedChildren(order);
        if (order.OrderStatus == OrderStatus.Cancelled)
            throw new ConflictException("Không thể ghi nhận thanh toán cho đơn đã hủy.");
        var succeeded = await _dbContext.OrderPayments
            .Where(payment => payment.OrderId == id && payment.Status == PaymentTransactionStatus.Succeeded)
            .ToListAsync(cancellationToken);
        var currentNet = succeeded.Sum(payment => payment.Kind == OrderPaymentKind.Refund ? -payment.Amount : payment.Amount);
        var nextNet = currentNet + (request.Kind == OrderPaymentKind.Refund ? -request.Amount : request.Amount);
        if (nextNet < 0)
            throw Validation(nameof(request.Amount), "Số tiền hoàn không thể lớn hơn số tiền đã nhận.");
        if (request.Kind != OrderPaymentKind.Refund && nextNet > order.TotalAmount)
            throw Validation(nameof(request.Amount), "Số tiền nhận vượt quá số còn phải thu.");

        var actor = CurrentActor();
        var now = UtcNow();
        var payment = new OrderPayment(
            id,
            request.Amount,
            request.Kind,
            request.Method,
            request.PaidAt.UtcDateTime,
            request.IdempotencyKey,
            request.Note,
            now,
            actor.Id,
            request.AffectsRecognizedRevenue,
            request.ProductRevenueAdjustment,
            request.ShippingRevenueAdjustment);
        if (_financePeriodGuard is not null)
            await _financePeriodGuard.InvalidateAsync(LocalBusinessDate(request.PaidAt.UtcDateTime), "Giao dịch thu/hoàn tiền đơn hàng", cancellationToken);
        _dbContext.OrderPayments.Add(payment);

        var targetStatus = nextNet <= 0
            ? PaymentStatus.Unpaid
            : nextNet >= order.TotalAmount ? PaymentStatus.Paid : PaymentStatus.Deposited;
        if (targetStatus != order.PaymentStatus)
            order.ReconcilePaymentStatus(
                targetStatus,
                now,
                actor.Id,
                actor.Name,
                targetStatus == PaymentStatus.Paid ? request.PaidAt.UtcDateTime : null,
                "Cập nhật từ giao dịch thu/hoàn tiền thực tế.");
        TrackAddedChildren(order, trackedChildren);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var duplicate = await _dbContext.OrderPayments.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (duplicate is not null && duplicate.OrderId == id)
                return ToPaymentDto(duplicate);
            throw new ConflictException("Giao dịch bị trùng hoặc dữ liệu thanh toán đã thay đổi. Vui lòng tải lại.");
        }
        return ToPaymentDto(payment);
    }

    public async Task ChangePaymentStatusAsync(
        Guid id,
        ChangePaymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        ValidateEnum<PaymentStatus>(request.Status, nameof(request.Status));
        if (request.Status == PaymentStatus.Unpaid)
            throw Validation(nameof(request.Status), "Không thể xóa lịch sử thu tiền bằng cách đổi trạng thái.");
        if (!request.PaidAt.HasValue || !request.Method.HasValue || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw Validation(nameof(request.PaidAt), "Cần xác nhận số tiền, thời điểm nhận, phương thức và mã chống trùng.");

        var order = await _dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Order), id);
        var paid = await NetPaidAsync(id, cancellationToken);
        var amount = request.Amount ?? (request.Status == PaymentStatus.Paid ? order.TotalAmount - paid : 0);
        if (amount <= 0)
            throw Validation(nameof(request.Amount), "Số tiền nhận thêm phải lớn hơn 0.");
        var kind = request.Status == PaymentStatus.Paid
            ? paid > 0 ? OrderPaymentKind.Remainder : OrderPaymentKind.Full
            : OrderPaymentKind.Deposit;
        await AddPaymentAsync(id, new CreateOrderPaymentRequest(
            amount,
            kind,
            request.Method.Value,
            request.PaidAt.Value,
            request.IdempotencyKey!,
            request.Note), cancellationToken);
    }

    [Obsolete("Use the transaction-backed overload so an actual received time is persisted.")]
    public async Task ChangePaymentStatusAsync(
        Guid id,
        PaymentStatus status,
        CancellationToken cancellationToken)
    {
        ValidateEnum<PaymentStatus>(status, nameof(status));
        var actor = CurrentActor();
        var order = await GetAggregateAsync(id, true, cancellationToken);
        var trackedChildren = CaptureTrackedChildren(order);
        order.ChangePaymentStatus(status, UtcNow(), actor.Id, actor.Name, "Legacy status-only update; payment history requires supplementation.");
        TrackAddedChildren(order, trackedChildren);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Đơn hàng đã được xử lý bởi người khác, vui lòng tải lại.");
        }
    }

    public async Task UpdateShippingFeeActualAsync(
        Guid id,
        UpdateShippingFeeActualRequest request,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        var order = await GetAggregateAsync(id, true, cancellationToken);
        ApplyExpectedRowVersion(order, request.RowVersion);
        if (_financePeriodGuard is not null)
        {
            if (order.ShippingFeeIncurredAt.HasValue)
                await _financePeriodGuard.InvalidateAsync(LocalBusinessDate(order.ShippingFeeIncurredAt.Value), "Sửa phí giao hàng", cancellationToken);
            if (request.IncurredAt.HasValue)
                await _financePeriodGuard.InvalidateAsync(LocalBusinessDate(request.IncurredAt.Value.UtcDateTime), "Sửa phí giao hàng", cancellationToken);
            if (request.PaidAt.HasValue)
                await _financePeriodGuard.InvalidateAsync(LocalBusinessDate(request.PaidAt.Value.UtcDateTime), "Thanh toán phí giao hàng", cancellationToken);
        }
        var trackedChildren = CaptureTrackedChildren(order);
        order.UpdateShippingFeeActual(
            request.ShippingFeeActual,
            UtcNow(),
            actor.Id,
            actor.Name,
            request.IncurredAt?.UtcDateTime,
            request.PaidAt?.UtcDateTime,
            request.Method,
            request.IdempotencyKey);
        TrackAddedChildren(order, trackedChildren);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Đơn hàng đã được xử lý bởi người khác, vui lòng tải lại.");
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var order = await GetAggregateAsync(id, true, cancellationToken);
        await InvalidateOrderDateAsync(order, "Xóa đơn hàng", cancellationToken);
        var imageUrls = order.Images.Select(image => image.ImageUrl).ToList();

        if (order.InventoryReserved)
        {
            var quantities = order.Items
                .Where(item => item.ProductId.HasValue)
                .GroupBy(item => item.ProductId!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
            var products = await _dbContext.Products
                .Where(product => quantities.Keys.Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, cancellationToken);
            if (products.Count != quantities.Count)
                throw new ConflictException("Một hoặc nhiều sản phẩm trong đơn không còn tồn tại.");
            foreach (var (productId, quantity) in quantities)
                products[productId].RestoreStock(quantity);
        }

        var now = UtcNow();
        await ApplyMaterialLifecycleAsync(order, OrderStatus.Cancelled, CurrentActor().Id, now, cancellationToken);
        var orderMaterials = await _dbContext.OrderMaterials
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        _dbContext.OrderMaterials.RemoveRange(orderMaterials);

        _dbContext.Orders.Remove(order);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("Đơn hàng đã được xử lý bởi người khác, vui lòng tải lại.");
        }

        await DeleteFilesBestEffortAsync(imageUrls);
    }

    public async Task<IReadOnlyList<OrderCalendarItemDto>> CalendarAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = BusinessDayUtc(date);
        var orders = await _dbContext.Orders.AsNoTracking()
            .Where(order => (order.DeliveryAt >= fromUtc && order.DeliveryAt < toUtc) || order.PreparationDate == date)
            .OrderBy(order => order.DeliveryAt)
            .ToListAsync(cancellationToken);
        return orders.Select(order => new OrderCalendarItemDto(
            order.Id,
            order.OrderCode,
            order.OrdererName,
            order.OrdererPhone,
            order.RecipientName,
            order.RecipientPhone,
            UtcOffset(order.DeliveryAt),
            UtcOffset(order.DeliveryTo),
            order.PreparationDate,
            order.PickupAtShop,
            order.ProvinceShipping,
            order.DeliveryAddress,
            order.OrderStatus,
            order.PaymentStatus,
            order.TotalAmount)).ToList();
    }

    public async Task<IReadOnlyList<OrderDeliveryLocationDto>> CalendarLocationsAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = BusinessDayUtc(date);
        var orders = await _dbContext.Orders.AsNoTracking()
            .Where(order =>
                order.DeliveryAt >= fromUtc && order.DeliveryAt < toUtc &&
                !order.PickupAtShop &&
                !order.ProvinceShipping &&
                order.DeliveryLatitude.HasValue && order.DeliveryLongitude.HasValue)
            .OrderBy(order => order.DeliveryAt)
            .ToListAsync(cancellationToken);
        return orders.Select(order => new OrderDeliveryLocationDto(
            order.Id,
            order.OrderCode,
            order.RecipientName ?? string.Empty,
            order.DeliveryAddress,
            order.DeliveryLatitude!.Value,
            order.DeliveryLongitude!.Value,
            UtcOffset(order.DeliveryAt),
            order.OrderStatus)).ToList();
    }

    private async Task<Order> BuildOrderAsync(
        CreateOrderForm form,
        ICollection<string> uploadedUrls,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        var now = UtcNow();
        var channelId = !form.ChannelId.HasValue || form.ChannelId.Value == Guid.Empty
            ? Channel.AdminId
            : form.ChannelId.Value;
        await EnsureActiveChannelAsync(channelId, cancellationToken);
        var snapshots = await ResolveSnapshotsAsync(form.Items, now, cancellationToken);
        var customer = await ResolveCustomerAsync(
            form.CustomerId,
            form.CreateMember,
            form.OrdererName,
            form.OrdererPhone ?? string.Empty,
            now,
            cancellationToken);
        var orderCode = await CreateUniqueOrderCodeAsync(now, cancellationToken);
        var address = await ResolveAddressSnapshotAsync(
            form.PickupAtShop,
            form.AddressScheme,
            form.ProvinceCode,
            form.DistrictCode,
            form.CommuneCode,
            form.AddressDetail,
            form.FullAddressSnapshot ?? form.DeliveryAddress,
            cancellationToken);
        var order = new Order(
            orderCode,
            channelId,
            customer?.Id,
            ToDetails(form, snapshots, address),
            snapshots,
            now,
            actor.Id,
            actor.Name,
            form.IsPaid);

        await ValidateImagesAsync(form.Images, order.Items.Count, cancellationToken);
        foreach (var image in form.Images.Where(item => item.ImageFile is { Length: > 0 }))
        {
            var file = image.ImageFile!;
            var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
            var objectPath = $"orders/{order.Id:N}/{image.OrderItemIndex:D3}-{image.SortOrder:D3}-{Guid.NewGuid():N}{extension}";
            await using var stream = file.OpenReadStream();
            var url = await _fileStorage.UploadPublicAsync(stream, objectPath, file.ContentType, cancellationToken);
            uploadedUrls.Add(url);
            var orderItem = order.Items.ElementAt(image.OrderItemIndex);
            order.AddImage(orderItem.Id, url, image.SortOrder);
        }
        return order;
    }

    private void AddInitialPayment(CreateOrderForm form, Order order)
    {
        if (form.IsPaid && !form.ConfirmPaymentReceived)
            throw Validation(nameof(form.ConfirmPaymentReceived), "Cần xác nhận đã thực nhận tiền trước khi đánh dấu đã thanh toán.");
        if (!form.ConfirmPaymentReceived) return;

        var amount = form.IsPaid ? order.TotalAmount : order.DepositAmount;
        if (amount <= 0)
            throw Validation(nameof(form.DepositAmount), "Số tiền đã nhận phải lớn hơn 0.");
        if (!form.PaymentMethod.HasValue)
            throw Validation(nameof(form.PaymentMethod), "Vui lòng chọn phương thức nhận tiền.");
        ValidateEnum<PaymentMethod>(form.PaymentMethod.Value, nameof(form.PaymentMethod));

        var actor = CurrentActor();
        var now = UtcNow();
        var payment = new OrderPayment(
            order.Id,
            amount,
            form.IsPaid ? OrderPaymentKind.Full : OrderPaymentKind.Deposit,
            form.PaymentMethod.Value,
            form.PaymentReceivedAt?.UtcDateTime ?? now,
            string.IsNullOrWhiteSpace(form.PaymentIdempotencyKey)
                ? $"order-create-{order.Id:N}"
                : form.PaymentIdempotencyKey,
            "Ghi nhận khi tạo đơn",
            now,
            actor.Id);
        _dbContext.OrderPayments.Add(payment);
        order.ChangePaymentStatus(
            form.IsPaid ? PaymentStatus.Paid : PaymentStatus.Deposited,
            now,
            actor.Id,
            actor.Name,
            "Ghi nhận giao dịch thu tiền khi tạo đơn.");
    }

    private async Task<Order> GetAggregateAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<Order> query = _dbContext.Orders
            .Include(order => order.Items)
                .ThenInclude(item => item.IngredientSnapshots)
            .Include(order => order.Images)
            .Include(order => order.ChangeLogs)
            .AsSplitQuery();
        if (!tracked)
            query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(order => order.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Order), id);
    }

    private async Task EnsureActiveChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        if (channelId == Guid.Empty)
            throw Validation(nameof(channelId), "Channel is required.");
        var channel = await _dbContext.Channels.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == channelId, cancellationToken)
            ?? throw new NotFoundException(nameof(Channel), channelId);
        if (!channel.IsActive)
            throw new ConflictException("The selected channel is inactive.");
    }

    private async Task<Customer?> ResolveCustomerAsync(
        Guid? customerId,
        bool? createMember,
        string name,
        string phone,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (customerId.HasValue)
        {
            return await _dbContext.Customers
                .SingleOrDefaultAsync(customer => customer.Id == customerId && customer.IsActive, cancellationToken)
                ?? throw new NotFoundException(nameof(Customer), customerId.Value);
        }

        if (createMember == false || (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone)))
            return null;

        // Requests that predate CreateMember keep the former exact-phone linking behavior.
        // Explicit new Admin requests never infer a link: staff must choose the existing Member.
        var legacyRequest = !createMember.HasValue;
        if (string.IsNullOrWhiteSpace(phone))
        {
            if (legacyRequest)
                return null;
            var nameOnlyCustomer = new Customer(name, string.Empty, null, null, now);
            _dbContext.Customers.Add(nameOnlyCustomer);
            return nameOnlyCustomer;
        }

        var normalizedPhone = Customer.NormalizePhone(phone);
        var matches = await _dbContext.Customers
            .Where(customer => customer.IsActive && customer.NormalizedPhone == normalizedPhone)
            .OrderBy(customer => customer.CreatedAt)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (matches.Count == 1)
        {
            if (legacyRequest)
                return matches[0];
            throw new ConflictException("A Member with this phone already exists. Select that Member before saving the order.");
        }

        if (matches.Count > 1)
            throw new ConflictException("Multiple Members use this phone. Select the intended Member before saving the order.");

        var customer = new Customer(name, phone, null, null, now);
        _dbContext.Customers.Add(customer);
        return customer;
    }

    private async Task<IReadOnlyList<OrderItemSnapshot>> ResolveSnapshotsAsync(
        IReadOnlyCollection<OrderLineRequest> lines,
        DateTime capturedAtUtc,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<Guid, OrderItem>? existingItems = null)
    {
        if (lines.Count == 0)
            throw Validation(nameof(lines), "At least one order item is required.");

        var ids = new HashSet<int>();
        var categoryIds = new HashSet<int>();
        var productTypeIds = new HashSet<int>();
        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw Validation("items", "Số lượng sản phẩm phải là số nguyên lớn hơn 0.");
            if (!string.IsNullOrWhiteSpace(line.ProductId))
            {
                if (!int.TryParse(line.ProductId, NumberStyles.None, CultureInfo.InvariantCulture, out var productId) || productId <= 0)
                    throw Validation(nameof(line.ProductId), "Product id must be a positive integer.");
                ids.Add(productId);
            }
            if (!string.IsNullOrWhiteSpace(line.ProductSku))
                skus.Add(line.ProductSku.Trim());
            if (line.CategoryId is <= 0)
                throw Validation(nameof(line.CategoryId), "Category id must be a positive integer.");
            if (line.CategoryId.HasValue)
                categoryIds.Add(line.CategoryId.Value);
            if (line.ProductTypeId is <= 0)
                throw Validation(nameof(line.ProductTypeId), "Product type id must be a positive integer.");
            if (line.ProductTypeId.HasValue)
                productTypeIds.Add(line.ProductTypeId.Value);
            if (line.Ingredients is { Count: > 100 })
                throw Validation(nameof(line.Ingredients), "An order item cannot contain more than 100 ingredients.");
            if (line.Ingredients is not null)
            {
                if (line.Ingredients.Any(item => item.IngredientId <= 0
                    || item.BaseQuantity <= 0
                    || item.SortOrder < 0))
                    throw Validation(nameof(line.Ingredients), "Every ingredient requires a valid id, positive quantity, and non-negative sort order.");
                if (line.Ingredients.Select(item => item.IngredientId).Distinct().Count() != line.Ingredients.Count)
                    throw Validation(nameof(line.Ingredients), "Ingredient ids must be unique within an order item.");
            }
        }

        var products = await _dbContext.Products.AsNoTracking()
            .Include(product => product.Translations)
            .Include(product => product.Ingredients)
            .Where(product => ids.Contains(product.Id) || skus.Contains(product.Sku))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        foreach (var productTypeId in products.Select(product => product.ProductTypeId).OfType<int>())
            productTypeIds.Add(productTypeId);
        foreach (var categoryId in products.Select(product => product.CategoryId))
            categoryIds.Add(categoryId);
        var categories = await _dbContext.Categories.AsNoTracking()
            .Include(category => category.Translations)
            .Where(category => categoryIds.Contains(category.Id))
            .ToDictionaryAsync(category => category.Id, cancellationToken);
        var productTypes = await _dbContext.ProductTypes.AsNoTracking()
            .Include(productType => productType.Translations)
            .Where(productType => productTypeIds.Contains(productType.Id))
            .ToDictionaryAsync(productType => productType.Id, cancellationToken);
        var ingredientIds = products
            .SelectMany(product => product.Ingredients)
            .Select(recipe => recipe.IngredientId)
            .Concat(lines.SelectMany(line => line.Ingredients ?? []).Select(item => item.IngredientId))
            .Distinct()
            .ToArray();
        var ingredientSnapshots = await (
                from ingredient in _dbContext.Ingredients.AsNoTracking()
                join unit in _dbContext.MeasurementUnits.AsNoTracking()
                    on ingredient.BaseUnitId equals unit.Id
                where ingredientIds.Contains(ingredient.Id)
                select new IngredientCatalogSnapshot(
                    ingredient.Id,
                    ingredient.Code,
                    ingredient.Name,
                    unit.Code,
                    unit.Name,
                    unit.Symbol))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var byId = products.ToDictionary(product => product.Id);
        var bySku = products.GroupBy(product => product.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var snapshots = new List<OrderItemSnapshot>(lines.Count);

        foreach (var line in lines)
        {
            if (line.UnitPrice < 0)
                throw Validation("items", "Đơn giá sản phẩm không được âm.");
            Product? product = null;
            if (!string.IsNullOrWhiteSpace(line.ProductId))
            {
                var productId = int.Parse(line.ProductId, CultureInfo.InvariantCulture);
                if (!byId.TryGetValue(productId, out product))
                    throw new NotFoundException(nameof(Product), productId);
            }
            else if (!string.IsNullOrWhiteSpace(line.ProductSku))
            {
                bySku.TryGetValue(line.ProductSku.Trim(), out product);
            }

            OrderItem? existingItem = null;
            if (!string.IsNullOrWhiteSpace(line.Id))
            {
                if (!Guid.TryParse(line.Id, out var existingItemId))
                    throw Validation(nameof(line.Id), "Order item id must be a valid GUID.");
                if (existingItems is null || !existingItems.TryGetValue(existingItemId, out existingItem))
                    throw Validation(nameof(line.Id), "Order item id does not belong to this order.");
            }

            var resolvedCategoryId = product?.CategoryId ?? line.CategoryId;
            Category? resolvedCategory = null;
            if (resolvedCategoryId.HasValue)
            {
                if (!categories.TryGetValue(resolvedCategoryId.Value, out resolvedCategory))
                    throw new NotFoundException(nameof(Category), resolvedCategoryId.Value);
                if (product is null && !resolvedCategory.IsActive)
                    throw new ConflictException($"Danh mục #{resolvedCategory.Id} đã ngừng hoạt động.");
            }
            var resolvedCategoryName = resolvedCategory?.Translations
                .OrderByDescending(translation => translation.LanguageCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
                .ThenBy(translation => translation.LanguageCode)
                .Select(translation => translation.Name)
                .FirstOrDefault();

            var resolvedProductTypeId = product?.ProductTypeId ?? line.ProductTypeId;
            ProductType? resolvedProductType = null;
            if (resolvedProductTypeId.HasValue)
            {
                if (!productTypes.TryGetValue(resolvedProductTypeId.Value, out resolvedProductType))
                    throw new NotFoundException(nameof(ProductType), resolvedProductTypeId.Value);
                if (product is null && !resolvedProductType.IsActive)
                    throw new ConflictException($"Dòng sản phẩm '{resolvedProductType.Code}' đã ngừng hoạt động.");
            }
            var resolvedProductTypeName = resolvedProductType?.Translations
                .OrderByDescending(translation => translation.LanguageCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
                .ThenBy(translation => translation.LanguageCode)
                .Select(translation => translation.Name)
                .FirstOrDefault() ?? resolvedProductType?.Code;

            var productIdentityUnchanged = existingItem is not null
                && existingItem.ProductId == product?.Id;
            var ingredientsSpecified = line.IngredientsSpecified || line.Ingredients is not null;
            var requestedRecipe = !ingredientsSpecified
                ? null
                : ResolveRequestedIngredientRecipe(line.Ingredients ?? [], ingredientSnapshots);

            if (product is not null)
            {
                if (!product.IsActive)
                    throw new ConflictException($"Sản phẩm '{product.Sku}' đã ngừng hoạt động.");
                var name = product.Translations
                    .OrderByDescending(translation => translation.LanguageCode.Equals("vi", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(translation => translation.LanguageCode)
                    .Select(translation => translation.Name)
                    .FirstOrDefault() ?? line.ProductName;
                var recipe = requestedRecipe
                    ?? (productIdentityUnchanged
                        ? null
                        : ResolveProductIngredientRecipe(product, ingredientSnapshots));
                snapshots.Add(new OrderItemSnapshot(
                    product.Id,
                    product.Sku,
                    name,
                    product.ThumbnailUrl,
                    line.UnitPrice > 0 ? line.UnitPrice : product.SalePrice ?? product.Price,
                    line.Quantity,
                    0,
                    line.Note,
                    line.HasCard,
                    line.CardMessage,
                    line.HasBanner,
                    line.BannerMessage,
                    recipe,
                    recipe is null ? null : capturedAtUtc,
                    resolvedProductTypeId,
                    resolvedProductTypeName,
                    resolvedCategoryId,
                    resolvedCategoryName));
            }
            else
            {
                if (line.UnitPrice <= 0)
                    throw Validation("items", "Đơn giá sản phẩm ngoài danh mục phải lớn hơn 0.");
                var recipe = requestedRecipe
                    ?? (productIdentityUnchanged ? null : []);
                snapshots.Add(new OrderItemSnapshot(
                    null,
                    line.ProductSku,
                    line.ProductName,
                    null,
                    line.UnitPrice,
                    line.Quantity,
                    0,
                    line.Note,
                    line.HasCard,
                    line.CardMessage,
                    line.HasBanner,
                    line.BannerMessage,
                    recipe,
                    recipe is null ? null : capturedAtUtc,
                    resolvedProductTypeId,
                    resolvedProductTypeName,
                    resolvedCategoryId,
                    resolvedCategoryName));
            }
        }

        return snapshots;
    }

    private static IReadOnlyList<IngredientRecipeSnapshot> ResolveProductIngredientRecipe(
        Product product,
        IReadOnlyDictionary<int, IngredientCatalogSnapshot> ingredientSnapshots) =>
        product.Ingredients
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item =>
            {
                if (!ingredientSnapshots.TryGetValue(item.IngredientId, out var ingredient))
                {
                    throw new ConflictException(
                        $"Product '{product.Sku}' has an ingredient recipe with missing master data.");
                }
                return new IngredientRecipeSnapshot(
                    ingredient.Id,
                    ingredient.Code,
                    ingredient.Name,
                    ingredient.UnitCode,
                    ingredient.UnitName,
                    ingredient.UnitSymbol,
                    item.BaseQuantity,
                    item.Note,
                    item.SortOrder);
            })
            .ToArray();

    private static IReadOnlyList<IngredientRecipeSnapshot> ResolveRequestedIngredientRecipe(
        IReadOnlyCollection<OrderLineIngredientRequest> requested,
        IReadOnlyDictionary<int, IngredientCatalogSnapshot> ingredientSnapshots) =>
        requested
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.IngredientId)
            .Select(item =>
            {
                if (!ingredientSnapshots.TryGetValue(item.IngredientId, out var ingredient))
                    throw new NotFoundException(nameof(Ingredient), item.IngredientId);
                return new IngredientRecipeSnapshot(
                    ingredient.Id,
                    ingredient.Code,
                    ingredient.Name,
                    ingredient.UnitCode,
                    ingredient.UnitName,
                    ingredient.UnitSymbol,
                    item.BaseQuantity,
                    item.Note,
                    item.SortOrder);
            })
            .ToArray();

    private sealed record IngredientCatalogSnapshot(
        int Id,
        string Code,
        string Name,
        string UnitCode,
        string UnitName,
        string? UnitSymbol);

    private async Task<string> CreateUniqueOrderCodeAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(2));
            var code = $"L{now:yyMMdd}-{suffix}";
            if (!await _dbContext.Orders.AnyAsync(order => order.OrderCode == code, cancellationToken))
                return code;
        }
        throw new ConflictException("A unique order code could not be generated. Please retry.");
    }

    private async Task<OrderAddressSnapshot> ResolveAddressSnapshotAsync(
        bool pickupAtShop,
        AdministrativeScheme? scheme,
        string? provinceCode,
        string? districtCode,
        string? communeCode,
        string? addressDetail,
        string? unstructuredAddress,
        CancellationToken cancellationToken)
    {
        if (pickupAtShop)
            return OrderAddressSnapshot.Empty;

        var normalizedProvinceCode = TrimToNull(provinceCode);
        var normalizedDistrictCode = TrimToNull(districtCode);
        var normalizedCommuneCode = TrimToNull(communeCode);
        var normalizedDetail = TrimToNull(addressDetail);
        var normalizedUnstructured = TrimToNull(unstructuredAddress);
        var hasStructuredCode = normalizedProvinceCode is not null
            || normalizedDistrictCode is not null
            || normalizedCommuneCode is not null;

        if (!scheme.HasValue && !hasStructuredCode)
        {
            return new OrderAddressSnapshot(
                null, null, null, null, null, null, null,
                normalizedDetail, normalizedUnstructured);
        }
        if (!scheme.HasValue || !Enum.IsDefined(scheme.Value))
            throw Validation("addressScheme", "Loại địa chỉ không hợp lệ.");
        if (normalizedProvinceCode is null)
            throw Validation("provinceCode", "Vui lòng chọn tỉnh hoặc thành phố.");
        if (normalizedCommuneCode is null)
            throw Validation("communeCode", "Vui lòng chọn xã, phường hoặc đặc khu.");
        if (scheme == AdministrativeScheme.Current && normalizedDistrictCode is not null)
            throw Validation("districtCode", "Địa chỉ mới không có cấp quận/huyện.");
        if (scheme == AdministrativeScheme.Legacy && normalizedDistrictCode is null)
            throw Validation("districtCode", "Địa chỉ cũ cần quận, huyện, thị xã hoặc thành phố.");

        var requestedCodes = new[] { normalizedProvinceCode, normalizedDistrictCode, normalizedCommuneCode }
            .Where(code => code is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var units = await _dbContext.AdministrativeUnits.AsNoTracking()
            .Where(unit => unit.Scheme == scheme.Value && unit.IsActive && requestedCodes.Contains(unit.Code))
            .ToDictionaryAsync(unit => unit.Code, StringComparer.Ordinal, cancellationToken);

        if (!units.TryGetValue(normalizedProvinceCode, out var province) || province.HierarchyLevel != 1)
            throw Validation("provinceCode", "Tỉnh hoặc thành phố không tồn tại trong bộ dữ liệu đã chọn.");
        if (!units.TryGetValue(normalizedCommuneCode, out var commune))
            throw Validation("communeCode", "Xã, phường hoặc đặc khu không tồn tại trong bộ dữ liệu đã chọn.");

        AdministrativeUnit? district = null;
        if (scheme == AdministrativeScheme.Current)
        {
            if (commune.HierarchyLevel != 2 || !string.Equals(commune.ParentCode, province.Code, StringComparison.Ordinal))
                throw Validation("communeCode", "Xã, phường hoặc đặc khu không thuộc tỉnh/thành phố đã chọn.");
        }
        else
        {
            if (!units.TryGetValue(normalizedDistrictCode!, out district) || district.HierarchyLevel != 2)
                throw Validation("districtCode", "Quận, huyện, thị xã hoặc thành phố không tồn tại.");
            if (!string.Equals(district.ParentCode, province.Code, StringComparison.Ordinal))
                throw Validation("districtCode", "Đơn vị cấp huyện không thuộc tỉnh/thành phố đã chọn.");
            if (commune.HierarchyLevel != 3 || !string.Equals(commune.ParentCode, district.Code, StringComparison.Ordinal))
                throw Validation("communeCode", "Xã, phường hoặc thị trấn không thuộc đơn vị cấp huyện đã chọn.");
        }

        var canonicalAddress = string.Join(", ", new[]
        {
            normalizedDetail,
            commune.FullName,
            district?.FullName,
            province.FullName
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var fullAddress = normalizedUnstructured ?? canonicalAddress;

        return new OrderAddressSnapshot(
            scheme,
            province.Code,
            province.FullName,
            district?.Code,
            district?.FullName,
            commune.Code,
            commune.FullName,
            normalizedDetail,
            fullAddress);
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OrderDetails ToDetails(
        CreateOrderForm form,
        IReadOnlyCollection<OrderItemSnapshot> items,
        OrderAddressSnapshot address)
    {
        var shippingFee = form.PickupAtShop ? 0 : form.ShippingFee;
        var orderValue = items.Sum(item => item.UnitPrice * item.Quantity - item.DiscountAmount) + shippingFee;
        var deposit = DefaultDepositCalculator.Resolve(orderValue, form.DepositAmount);
        return new(
        form.OrdererName,
        form.OrdererPhone ?? string.Empty,
        form.RecipientName,
        form.RecipientPhone ?? string.Empty,
        form.PickupAtShop,
        form.ProvinceShipping,
        address.FullAddressSnapshot,
        form.DeliveryAddressDescription,
        form.DeliveryLatitude,
        form.DeliveryLongitude,
        form.DeliveryAt.UtcDateTime,
        form.DeliveryTo?.UtcDateTime,
        deposit,
        form.ShippingFee,
        form.ShippingFeeActual,
        form.Description,
        form.ContentNote,
        address.Scheme,
        address.ProvinceCode,
        address.ProvinceName,
        address.DistrictCode,
        address.DistrictName,
        address.CommuneCode,
        address.CommuneName,
        address.AddressDetail,
        address.FullAddressSnapshot,
        form.SenderName ?? string.Empty,
        form.SenderPhone ?? string.Empty);
    }

    private static OrderDetails ToDetails(UpdateOrderForm form, OrderAddressSnapshot address) => new(
        form.OrdererName,
        form.OrdererPhone ?? string.Empty,
        form.RecipientName,
        form.RecipientPhone ?? string.Empty,
        form.PickupAtShop,
        form.ProvinceShipping,
        address.FullAddressSnapshot,
        form.DeliveryAddressDescription,
        form.DeliveryLatitude,
        form.DeliveryLongitude,
        form.DeliveryAt.UtcDateTime,
        form.DeliveryTo?.UtcDateTime,
        form.DepositAmount,
        form.ShippingFee,
        form.ShippingFeeActual,
        form.Description,
        form.ContentNote,
        address.Scheme,
        address.ProvinceCode,
        address.ProvinceName,
        address.DistrictCode,
        address.DistrictName,
        address.CommuneCode,
        address.CommuneName,
        address.AddressDetail,
        address.FullAddressSnapshot,
        form.SenderName ?? string.Empty,
        form.SenderPhone ?? string.Empty);

    internal static OrderListItemDto ToListDto(
        Order order,
        decimal paidAmount = 0,
        int paymentCount = 0,
        string? channelName = null,
        DateTime? nowUtc = null) => new(
        order.Id,
        order.OrderCode,
        order.OrdererName,
        order.OrdererPhone,
        order.ChannelId,
        order.RecipientName,
        order.RecipientPhone,
        order.PickupAtShop,
        order.ProvinceShipping,
        order.DeliveryAddress,
        UtcOffset(order.DeliveryAt),
        UtcOffset(order.DeliveryTo),
        order.DepositAmount,
        order.ShippingFee,
        order.ShippingFeeActual,
        order.SubTotal,
        order.TotalAmount,
        order.PaymentStatus,
        order.OrderStatus,
        UtcOffset(order.CreatedAt),
        order.ContentNote,
        order.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ThumbnailUrl))
            .OrderBy(item => item.Id)
            .Select(item => item.ThumbnailUrl)
            .FirstOrDefault()
        ?? order.Images
            .OrderBy(image => image.SortOrder)
            .Select(image => image.ImageUrl)
            .FirstOrDefault())
    {
        ProductSummary = string.Join(", ", order.Items
            .OrderBy(item => item.Id)
            .Select(item => item.ProductName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(2)),
        PaidAmount = paidAmount,
        RemainingAmount = Math.Max(0, order.TotalAmount - paidAmount),
        PaymentHistoryIncomplete = paymentCount == 0 && order.PaymentStatus != PaymentStatus.Unpaid,
        CompletedAt = UtcOffset(order.CompletedAt),
        ChannelName = channelName,
        DeliveredAt = UtcOffset(order.DeliveredAt),
        PaidInFullAt = UtcOffset(order.PaidInFullAt),
        PreparationDate = order.PreparationDate,
        AssigneeId = order.AssigneeId,
        AssigneeName = order.AssigneeName,
        HoldReason = order.HoldReason,
        HoldAt = UtcOffset(order.HoldAt),
        FulfillmentType = order.PickupAtShop ? "pickup" : order.ProvinceShipping ? "province-delivery" : "local-delivery",
        TrackingGroup = TrackingGroup(order, nowUtc ?? DateTime.UtcNow),
        QuantityTotal = order.Items.Sum(item => item.Quantity),
        HasCard = order.Items.Any(item => item.HasCard),
        CardMessage = string.Join(Environment.NewLine, order.Items.Where(item => item.HasCard && !string.IsNullOrWhiteSpace(item.CardMessage)).Select(item => item.CardMessage)),
        HasBanner = order.Items.Any(item => item.HasBanner),
        BannerMessage = string.Join(Environment.NewLine, order.Items.Where(item => item.HasBanner && !string.IsNullOrWhiteSpace(item.BannerMessage)).Select(item => item.BannerMessage)),
        RowVersion = order.RowVersion.Length == 0 ? null : Convert.ToBase64String(order.RowVersion)
    };

    private static OrderDetailDto ToDetailDto(Order order, IReadOnlyCollection<OrderPayment>? payments = null) => new(
        order.Id,
        order.OrderCode,
        order.OrdererName,
        order.OrdererPhone,
        order.ChannelId,
        order.RecipientName,
        order.RecipientPhone,
        order.PickupAtShop,
        order.ProvinceShipping,
        order.DeliveryAddress,
        order.DeliveryAddressDescription,
        order.AddressScheme,
        order.ProvinceCode,
        order.ProvinceName,
        order.DistrictCode,
        order.DistrictName,
        order.CommuneCode,
        order.CommuneName,
        order.AddressDetail,
        order.FullAddressSnapshot,
        UtcOffset(order.DeliveryAt),
        UtcOffset(order.DeliveryTo),
        order.DepositAmount,
        order.ShippingFee,
        order.ShippingFeeActual,
        order.SubTotal,
        order.TotalAmount,
        order.PaymentStatus,
        order.OrderStatus,
        UtcOffset(order.CreatedAt),
        order.DeliveryLatitude,
        order.DeliveryLongitude,
        order.Description,
        order.ContentNote,
        UtcOffset(order.UpdatedAt),
        order.RowVersion.Length == 0 ? null : Convert.ToBase64String(order.RowVersion),
        order.Items.OrderBy(item => item.Id).Select(item => new OrderItemDto(
            item.Id,
            item.ProductId?.ToString(CultureInfo.InvariantCulture),
            item.ProductSku,
            item.ProductName,
            item.ThumbnailUrl,
            item.UnitPrice,
            item.Quantity,
            item.LineTotal,
            item.Note,
            item.HasCard,
            item.CardMessage,
            item.HasBanner,
            item.BannerMessage,
            UtcOffset(item.IngredientSnapshotCapturedAtUtc),
            item.IngredientSnapshots
                .OrderBy(snapshot => snapshot.SortOrder)
                .ThenBy(snapshot => snapshot.Id)
                .Select(snapshot => new OrderItemIngredientSnapshotDto(
                    snapshot.Id,
                    snapshot.IngredientId,
                    snapshot.IngredientCode,
                    snapshot.IngredientName,
                    snapshot.BaseUnitCode,
                    snapshot.BaseUnitName,
                    snapshot.BaseUnitSymbol,
                    snapshot.PerProductBaseQuantity,
                    snapshot.ProductQuantity,
                    snapshot.TotalBaseQuantity,
                    snapshot.Note,
                    snapshot.SortOrder,
                    UtcOffset(snapshot.CapturedAtUtc)))
                .ToList(),
            order.Images.Where(image => image.OrderItemId == item.Id).OrderBy(image => image.SortOrder)
                .Select(image => new OrderImageDto(image.Id, image.OrderItemId, image.ImageUrl, image.SortOrder, image.Description)).ToList(),
            item.ProductTypeId,
            item.ProductTypeName,
            item.CategoryId,
            item.CategoryName)).ToList(),
        order.Images.OrderBy(image => image.SortOrder).Select(image => new OrderImageDto(
            image.Id,
            image.OrderItemId,
            image.ImageUrl,
            image.SortOrder,
            image.Description)).ToList(),
        order.ChangeLogs.OrderByDescending(log => log.ChangedAt).Select(log => new OrderChangeLogDto(
            log.Id,
            log.EntityName,
            log.FieldName,
            log.OldValue,
            log.NewValue,
            log.ChangeType,
            log.ChangedById,
            log.ChangedByName,
            UtcOffset(log.ChangedAt),
            log.Note)).ToList())
    {
        CustomerId = order.CustomerId,
        SenderName = order.SenderName,
        SenderPhone = order.SenderPhone,
        PaidAmount = NetPaid(payments ?? []),
        RemainingAmount = Math.Max(0, order.TotalAmount - NetPaid(payments ?? [])),
        PaymentHistoryIncomplete = (payments?.Count ?? 0) == 0 && order.PaymentStatus != PaymentStatus.Unpaid,
        Payments = (payments ?? []).Select(ToPaymentDto).ToList(),
        CompletedAt = UtcOffset(order.CompletedAt),
        ShippingFeeIncurredAt = UtcOffset(order.ShippingFeeIncurredAt),
        ShippingFeePaidAt = UtcOffset(order.ShippingFeePaidAt),
        ShippingFeePaymentMethod = order.ShippingFeePaymentMethod,
        DeliveredAt = UtcOffset(order.DeliveredAt),
        PaidInFullAt = UtcOffset(order.PaidInFullAt),
        PreparationDate = order.PreparationDate,
        AssigneeId = order.AssigneeId,
        AssigneeName = order.AssigneeName,
        HoldReason = order.HoldReason,
        HoldAt = UtcOffset(order.HoldAt),
        Checklist = new OrderChecklistDto(order.DesignConfirmed, order.MaterialsPrepared, order.CardPrepared, order.QualityChecked),
        CancelledReason = order.CancelledReason
    };

    private static string TrackingGroup(Order order, DateTime nowUtc)
    {
        if (order.OrderStatus == OrderStatus.Cancelled) return "cancelled";
        if (order.OrderStatus == OrderStatus.Completed) return "completed";
        if (order.OrderStatus == OrderStatus.Delivered && order.PaymentStatus != PaymentStatus.Paid) return "delivered-unpaid";
        if (order.DeliveryAt < nowUtc) return "late";
        var localDelivery = TimeZoneInfo.ConvertTimeFromUtc(order.DeliveryAt, BusinessTimeZone).Date;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, BusinessTimeZone).Date;
        return localDelivery == localNow ? "today" : "upcoming";
    }

    private static IQueryable<Order> ApplyFulfillmentFilter(IQueryable<Order> query, string? fulfillmentType)
    {
        if (string.IsNullOrWhiteSpace(fulfillmentType)) return query;
        return fulfillmentType.Trim().ToLowerInvariant() switch
        {
            "pickup" => query.Where(order => order.PickupAtShop),
            "local-delivery" => query.Where(order => !order.PickupAtShop && !order.ProvinceShipping),
            "province-delivery" => query.Where(order => !order.PickupAtShop && order.ProvinceShipping),
            _ => throw Validation(nameof(OrderListQuery.FulfillmentType), "Cách nhận hàng không hợp lệ.")
        };
    }

    private async Task<Dictionary<Guid, string>> ChannelNamesAsync(IEnumerable<Guid> channelIds, CancellationToken cancellationToken)
    {
        var ids = channelIds.Distinct().ToArray();
        return ids.Length == 0
            ? []
            : await _dbContext.Channels.AsNoTracking()
                .Where(channel => ids.Contains(channel.Id))
                .ToDictionaryAsync(channel => channel.Id, channel => channel.Name, cancellationToken);
    }

    private static IQueryable<Order> ApplyDateRange(
        IQueryable<Order> query,
        DateTimeOffset? from,
        DateTimeOffset? to,
        System.Linq.Expressions.Expression<Func<Order, DateTime>> selector)
    {
        if (!from.HasValue && !to.HasValue)
            return query;
        var parameter = selector.Parameters[0];
        var body = selector.Body;
        System.Linq.Expressions.Expression? predicate = null;
        if (from.HasValue)
        {
            var fromUtc = from.Value.UtcDateTime;
            predicate = System.Linq.Expressions.Expression.GreaterThanOrEqual(body, System.Linq.Expressions.Expression.Constant(fromUtc));
        }
        if (to.HasValue)
        {
            var toUtc = to.Value.UtcDateTime;
            var upper = System.Linq.Expressions.Expression.LessThanOrEqual(body, System.Linq.Expressions.Expression.Constant(toUtc));
            predicate = predicate is null ? upper : System.Linq.Expressions.Expression.AndAlso(predicate, upper);
        }
        return query.Where(System.Linq.Expressions.Expression.Lambda<Func<Order, bool>>(predicate!, parameter));
    }

    public static (DateTime FromUtc, DateTime ToUtc) BusinessDayUtc(DateOnly date)
    {
        var localStart = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, BusinessTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, BusinessTimeZone));
    }

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }
        return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07");
    }

    private static DateTimeOffset UtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? UtcOffset(DateTime? value) =>
        value.HasValue ? UtcOffset(value.Value) : null;

    private static DateTime BusinessStartUtc(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, BusinessTimeZone);
    }

    private async Task<Dictionary<Guid, PaymentTotal>> PaymentTotalsAsync(
        IEnumerable<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, PaymentTotal>();
        var rows = await _dbContext.OrderPayments.AsNoTracking()
            .Where(payment => ids.Contains(payment.OrderId)
                && payment.Status == PaymentTransactionStatus.Succeeded)
            .GroupBy(payment => payment.OrderId)
            .Select(group => new
            {
                OrderId = group.Key,
                Amount = group.Sum(payment => payment.Kind == OrderPaymentKind.Refund ? -payment.Amount : payment.Amount),
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.OrderId, row => new PaymentTotal(row.Amount, row.Count));
    }

    private async Task<decimal> NetPaidAsync(Guid orderId, CancellationToken cancellationToken) =>
        await _dbContext.OrderPayments.AsNoTracking()
            .Where(payment => payment.OrderId == orderId
                && payment.Status == PaymentTransactionStatus.Succeeded)
            .SumAsync(payment => payment.Kind == OrderPaymentKind.Refund ? -payment.Amount : payment.Amount, cancellationToken);

    private static decimal NetPaid(IEnumerable<OrderPayment> payments) =>
        payments.Where(payment => payment.Status == PaymentTransactionStatus.Succeeded)
            .Sum(payment => payment.Kind == OrderPaymentKind.Refund ? -payment.Amount : payment.Amount);

    private static OrderPaymentDto ToPaymentDto(OrderPayment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Amount,
        payment.Kind,
        payment.Method,
        UtcOffset(payment.PaidAt),
        payment.Status,
        payment.Note,
        payment.AffectsRecognizedRevenue,
        payment.ProductRevenueAdjustment,
        payment.ShippingRevenueAdjustment);

    private readonly record struct PaymentTotal(decimal Amount, int Count);

    private (Guid? Id, string? Name) CurrentActor()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var subject = principal?.FindFirstValue("sub") ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return (
            Guid.TryParse(subject, out var id) ? id : null,
            principal?.FindFirstValue("unique_name") ?? principal?.Identity?.Name);
    }

    private async Task InvalidateOrderDateAsync(Order order, string reason, CancellationToken cancellationToken)
    {
        if (_financePeriodGuard is not null && order.DeliveredAt.HasValue
            && order.OrderStatus is OrderStatus.Delivered or OrderStatus.Completed)
            await _financePeriodGuard.InvalidateAsync(LocalBusinessDate(order.DeliveredAt.Value), reason, cancellationToken);
    }

    private static DateOnly LocalBusinessDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BusinessTimeZone));

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static async Task ValidateImagesAsync(
        IReadOnlyCollection<CreateOrderImageForm> images,
        int itemCount,
        CancellationToken cancellationToken)
    {
        var supplied = images.Where(item => item.ImageFile is { Length: > 0 }).ToList();
        if (supplied.Count > ImageUploadPolicy.MaximumFileCount)
            throw Validation(nameof(images), $"At most {ImageUploadPolicy.MaximumFileCount} images are allowed.");
        foreach (var image in supplied)
        {
            var file = image.ImageFile!;
            if (!ImageUploadPolicy.HasAllowedMetadata(file))
                throw Validation(nameof(images), "Only JPG, PNG, WEBP, and GIF images up to 10 MB are allowed.");
            if (!await ImageUploadPolicy.HasValidSignatureAsync(file, cancellationToken))
                throw Validation(nameof(images), "Image content does not match its declared type.");
            if (image.SortOrder < 0)
                throw Validation(nameof(images), "Image sort order cannot be negative.");
            if (image.OrderItemIndex < 0 || image.OrderItemIndex >= itemCount)
                throw Validation(nameof(images), "Every image must reference a valid order item.");
        }
    }

    private static TrackedOrderChildren CaptureTrackedChildren(Order order) => new(
        order.Items.Select(item => item.Id).ToHashSet(),
        order.Images.Select(image => image.Id).ToHashSet(),
        order.ChangeLogs.Select(log => log.Id).ToHashSet(),
        order.Items.SelectMany(item => item.IngredientSnapshots).Select(item => item.Id).ToHashSet());

    private void ApplyExpectedRowVersion(Order order, string? encodedRowVersion)
    {
        if (string.IsNullOrWhiteSpace(encodedRowVersion))
            return;

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(encodedRowVersion);
        }
        catch (FormatException)
        {
            throw Validation(nameof(UpdateOrderForm.RowVersion), "Phiên bản đơn hàng không hợp lệ. Vui lòng tải lại.");
        }

        if (rowVersion.Length != 8)
            throw Validation(nameof(UpdateOrderForm.RowVersion), "Phiên bản đơn hàng không hợp lệ. Vui lòng tải lại.");

        _dbContext.Entry(order).Property(item => item.RowVersion).OriginalValue = rowVersion;
    }

    private void TrackAddedChildren(Order order, TrackedOrderChildren tracked)
    {
        _dbContext.OrderItems.AddRange(order.Items.Where(item => !tracked.ItemIds.Contains(item.Id)));
        _dbContext.OrderImages.AddRange(order.Images.Where(image => !tracked.ImageIds.Contains(image.Id)));
        _dbContext.OrderChangeLogs.AddRange(order.ChangeLogs.Where(log => !tracked.ChangeLogIds.Contains(log.Id)));
        _dbContext.OrderItemIngredientSnapshots.AddRange(
            order.Items.SelectMany(item => item.IngredientSnapshots)
                .Where(item => !tracked.IngredientSnapshotIds.Contains(item.Id)));
    }

    private async Task DeleteFilesBestEffortAsync(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            try
            {
                await _fileStorage.DeleteAsync(url, CancellationToken.None);
            }
            catch
            {
                // Storage cleanup must not hide the order result or the original application error.
            }
        }
    }

    private async Task ApplyMaterialLifecycleAsync(
        Order order,
        OrderStatus targetStatus,
        Guid? actorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var targetIsCommit = (order.OrderStatus is OrderStatus.Created or OrderStatus.Confirmed)
            && (targetStatus is OrderStatus.Producing or OrderStatus.Ready or OrderStatus.Shipping or OrderStatus.Delivered or OrderStatus.DeliveryFailed);
        var targetIsRestore = (targetStatus is OrderStatus.Created or OrderStatus.Confirmed or OrderStatus.Cancelled)
            && order.OrderStatus is not (OrderStatus.Created or OrderStatus.Confirmed or OrderStatus.Cancelled);
        if (!targetIsCommit && !targetIsRestore)
            return;

        var materials = await _dbContext.OrderMaterials
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        if (materials.Count == 0)
            return;
        var stockUnitIds = materials.Select(item => item.StockUnitId).Distinct().ToArray();
        var stockUnits = await _dbContext.InventoryStockUnits
            .Where(item => stockUnitIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (stockUnits.Count != stockUnitIds.Length)
            throw new ConflictException("Một hoặc nhiều size nguyên liệu của đơn hàng không còn tồn tại.");

        foreach (var material in materials)
        {
            var stockUnit = stockUnits[material.StockUnitId];
            var targetQuantity = targetIsCommit ? material.RequiredQuantity : 0;
            var delta = decimal.Round(targetQuantity - material.DeductedQuantity, 6, MidpointRounding.AwayFromZero);
            if (delta == 0)
                continue;
            if (delta > 0)
            {
                if (stockUnit.Quantity < delta)
                {
                    var size = string.IsNullOrWhiteSpace(material.SizeName) ? "không size" : $"size {material.SizeName}";
                    throw new ConflictException(
                        $"Vật tư kho '{material.InventoryItemName}' ({size}) không đủ tồn kho: cần {delta}, hiện còn {stockUnit.Quantity}.");
                }
                stockUnit.Remove(delta, now);
            }
            else
            {
                stockUnit.Add(-delta, now);
            }

            material.SetDeductedQuantity(targetQuantity, now);
            _dbContext.StockTransactions.Add(new StockTransaction(
                stockUnit.Id,
                delta > 0 ? StockTransactionType.OrderUsage : StockTransactionType.OrderReturn,
                -delta,
                stockUnit.Quantity,
                $"order:{order.Id:N}:material:{material.Id:N}:{Guid.NewGuid():N}",
                null,
                order.Id,
                material.Id,
                delta > 0
                    ? "Bắt đầu chuẩn bị đơn hàng."
                    : targetStatus == OrderStatus.Created
                        ? "Hoàn kho do trả đơn về trạng thái chờ xác nhận."
                        : "Hoàn kho do hủy hoặc xóa đơn hàng.",
                actorId,
                now));
        }
    }

    private static void ValidateDateRange(DateTimeOffset? from, DateTimeOffset? to, string field)
    {
        if (from.HasValue && to.HasValue && from > to)
            throw Validation(field, "The start date cannot be after the end date.");
    }

    private static void ValidateEnum<TEnum>(TEnum? value, string field) where TEnum : struct, Enum
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
            throw Validation(field, $"{field} is invalid.");
    }

    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private sealed record TrackedOrderChildren(
        HashSet<Guid> ItemIds,
        HashSet<Guid> ImageIds,
        HashSet<Guid> ChangeLogIds,
        HashSet<Guid> IngredientSnapshotIds);

    private sealed record OrderAddressSnapshot(
        AdministrativeScheme? Scheme,
        string? ProvinceCode,
        string? ProvinceName,
        string? DistrictCode,
        string? DistrictName,
        string? CommuneCode,
        string? CommuneName,
        string? AddressDetail,
        string? FullAddressSnapshot)
    {
        public static readonly OrderAddressSnapshot Empty = new(
            null, null, null, null, null, null, null, null, null);
    }
}
