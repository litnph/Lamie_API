using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Lamie.API.Models.Orders;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Lamie.Domain.Orders;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class OrderService : IOrderService
{
    private const int MaximumPageSize = 100;
    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();

    private readonly AppDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public OrderService(
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

        IQueryable<Order> query = _dbContext.Orders.AsNoTracking();
        if (request.OrderStatus.HasValue)
            query = query.Where(order => order.OrderStatus == request.OrderStatus.Value);
        if (request.PaymentStatus.HasValue)
            query = query.Where(order => order.PaymentStatus == request.PaymentStatus.Value);
        if (request.ChannelId.HasValue)
            query = query.Where(order => order.ChannelId == request.ChannelId.Value);

        query = ApplyDateRange(query, request.CreatedFrom, request.CreatedTo, order => order.CreatedAt);
        query = ApplyDateRange(query, request.DeliveryFrom, request.DeliveryTo, order => order.DeliveryAt);

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var phone = new string(request.Phone.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrWhiteSpace(phone))
            {
                query = query.Where(order =>
                    order.OrdererPhone.Replace(" ", "").Replace("-", "").Contains(phone) ||
                    order.RecipientPhone.Replace(" ", "").Replace("-", "").Contains(phone));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(order =>
                order.OrderCode.Contains(search) ||
                order.OrdererName.Contains(search) ||
                order.OrdererPhone.Contains(search) ||
                order.RecipientName.Contains(search) ||
                order.RecipientPhone.Contains(search) ||
                (order.DeliveryAddress != null && order.DeliveryAddress.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var orderedQuery = (request.SortBy, request.SortDirection) switch
        {
            (OrderSortBy.DeliveryAt, SortDirection.Ascending) => query.OrderBy(order => order.DeliveryAt).ThenBy(order => order.OrderCode),
            (OrderSortBy.DeliveryAt, SortDirection.Descending) => query.OrderByDescending(order => order.DeliveryAt).ThenByDescending(order => order.OrderCode),
            (OrderSortBy.CreatedAt, SortDirection.Ascending) => query.OrderBy(order => order.CreatedAt).ThenBy(order => order.OrderCode),
            (OrderSortBy.CreatedAt, SortDirection.Descending) => query.OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.OrderCode),
            (OrderSortBy.TotalAmount, SortDirection.Ascending) => query.OrderBy(order => order.TotalAmount).ThenBy(order => order.OrderCode),
            (OrderSortBy.TotalAmount, SortDirection.Descending) => query.OrderByDescending(order => order.TotalAmount).ThenByDescending(order => order.OrderCode),
            _ => query.OrderBy(order => order.DeliveryAt).ThenBy(order => order.OrderCode)
        };
        var orders = await orderedQuery
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(order => order.Items)
            .Include(order => order.Images)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new PagedOrdersDto(
            orders.Select(ToListDto).ToList(),
            totalCount,
            request.Page,
            request.PageSize,
            totalPages,
            request.Page < totalPages,
            request.Page > 1);
    }

    public async Task<OrderDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await GetAggregateAsync(id, false, cancellationToken);
        return ToDetailDto(order);
    }

    public async Task<OrderDetailDto> CreateAsync(CreateOrderForm form, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        var now = UtcNow();
        var channelId = !form.ChannelId.HasValue || form.ChannelId.Value == Guid.Empty
            ? Channel.AdminId
            : form.ChannelId.Value;
        await EnsureActiveChannelAsync(channelId, cancellationToken);
        var snapshots = await ResolveSnapshotsAsync(form.Items, cancellationToken);
        var customer = await FindOrCreateCustomerAsync(form.OrdererName, form.OrdererPhone ?? string.Empty, now, cancellationToken);
        var orderCode = await CreateUniqueOrderCodeAsync(now, cancellationToken);
        var order = new Order(
            orderCode,
            channelId,
            customer?.Id,
            ToDetails(form, snapshots),
            snapshots,
            now,
            actor.Id,
            actor.Name);

        var uploadedUrls = new List<string>();
        var committed = false;
        try
        {
            await ValidateImagesAsync(form.Images, order.Items.Count, cancellationToken);
            foreach (var image in form.Images.Where(item => item.ImageFile is { Length: > 0 }))
            {
                var file = image.ImageFile!;
                var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
                var objectPath = $"orders/{order.Id:N}/{image.SortOrder:D3}-{Guid.NewGuid():N}{extension}";
                await using var stream = file.OpenReadStream();
                var url = await _fileStorage.UploadPublicAsync(stream, objectPath, file.ContentType, cancellationToken);
                uploadedUrls.Add(url);
                var orderItem = order.Items.ElementAt(image.OrderItemIndex);
                order.AddImage(orderItem.Id, url, image.SortOrder);
            }
            EnsureManualItemsHaveImages(order);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            _dbContext.Orders.Add(order);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                committed = true;
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new ConflictException("The order could not be created because its generated code conflicted. Please retry.");
            }

            return ToDetailDto(order);
        }
        catch
        {
            foreach (var url in committed ? Enumerable.Empty<string>() : uploadedUrls)
            {
                try
                {
                    await _fileStorage.DeleteAsync(url, CancellationToken.None);
                }
                catch
                {
                    // Cleanup is best-effort; preserve the original application error.
                }
            }

            throw;
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
        var snapshots = await ResolveSnapshotsAsync(form.Items, cancellationToken);
        var order = await GetAggregateAsync(id, true, cancellationToken);
        ApplyExpectedRowVersion(order, form.RowVersion);
        var trackedChildren = CaptureTrackedChildren(order);
        var actor = CurrentActor();
        var now = UtcNow();
        var customer = await FindOrCreateCustomerAsync(form.OrdererName, form.OrdererPhone ?? string.Empty, now, cancellationToken);

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
            ToDetails(form),
            itemUpdates,
            now,
            actor.Id,
            actor.Name);

        var currentItems = order.Items.ToList();

        var uploadedUrls = new List<string>();
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
            EnsureManualItemsHaveImages(order);
            TrackAddedChildren(order, trackedChildren);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await DeleteFilesBestEffortAsync(uploadedUrls);
            throw new ConflictException("Đơn hàng đã được xử lý bởi người khác, vui lòng tải lại.");
        }
        catch
        {
            await DeleteFilesBestEffortAsync(uploadedUrls);
            throw;
        }

        await DeleteFilesBestEffortAsync(removedUrls);

        return ToDetailDto(order);
    }

    public async Task ChangeStatusAsync(Guid id, OrderStatus status, CancellationToken cancellationToken)
    {
        ValidateEnum<OrderStatus>(status, nameof(status));
        var actor = CurrentActor();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var order = await GetAggregateAsync(id, true, cancellationToken);
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

        order.ChangeStatus(status, inventoryReservedAfterTransition, UtcNow(), actor.Id, actor.Name);
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

    public async Task ChangePaymentStatusAsync(
        Guid id,
        PaymentStatus status,
        CancellationToken cancellationToken)
    {
        ValidateEnum<PaymentStatus>(status, nameof(status));
        var actor = CurrentActor();
        var order = await GetAggregateAsync(id, true, cancellationToken);
        var trackedChildren = CaptureTrackedChildren(order);
        order.ChangePaymentStatus(status, UtcNow(), actor.Id, actor.Name);
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
        var imageUrls = order.Images.Select(image => image.ImageUrl).ToList();

        if (order.InventoryReserved && order.OrderStatus is OrderStatus.Producing or OrderStatus.Shipping)
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
            .Where(order => order.DeliveryAt >= fromUtc && order.DeliveryAt < toUtc)
            .OrderBy(order => order.DeliveryAt)
            .ToListAsync(cancellationToken);
        return orders.Select(order => new OrderCalendarItemDto(
            order.Id,
            order.OrderCode,
            order.RecipientName,
            order.RecipientPhone,
            UtcOffset(order.DeliveryAt),
            UtcOffset(order.DeliveryTo),
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
            order.RecipientName,
            order.DeliveryAddress,
            order.DeliveryLatitude!.Value,
            order.DeliveryLongitude!.Value,
            UtcOffset(order.DeliveryAt),
            order.OrderStatus)).ToList();
    }

    private async Task<Order> GetAggregateAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        IQueryable<Order> query = _dbContext.Orders
            .Include(order => order.Items)
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

    private async Task<Customer?> FindOrCreateCustomerAsync(
        string name,
        string phone,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var normalizedPhone = Customer.NormalizePhone(phone);
        var matches = await _dbContext.Customers
            .Where(customer => customer.IsActive && customer.NormalizedPhone == normalizedPhone)
            .OrderBy(customer => customer.CreatedAt)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (matches.Count == 1)
        {
            var existing = matches[0];
            existing.Update(name, phone, existing.Email, existing.Notes, existing.IsActive, now);
            return existing;
        }

        var customer = new Customer(name, phone, null, null, now);
        _dbContext.Customers.Add(customer);
        return customer;
    }

    private async Task<IReadOnlyList<OrderItemSnapshot>> ResolveSnapshotsAsync(
        IReadOnlyCollection<OrderLineRequest> lines,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
            throw Validation(nameof(lines), "At least one order item is required.");

        var ids = new HashSet<int>();
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
        }

        var products = await _dbContext.Products.AsNoTracking()
            .Include(product => product.Translations)
            .Where(product => ids.Contains(product.Id) || skus.Contains(product.Sku))
            .ToListAsync(cancellationToken);
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

            if (product is not null)
            {
                if (!product.IsActive)
                    throw new ConflictException($"Sản phẩm '{product.Sku}' đã ngừng hoạt động.");
                var name = product.Translations
                    .OrderByDescending(translation => translation.LanguageCode.Equals("vi", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(translation => translation.LanguageCode)
                    .Select(translation => translation.Name)
                    .FirstOrDefault() ?? line.ProductName;
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
                    line.BannerMessage));
            }
            else
            {
                if (line.UnitPrice <= 0)
                    throw Validation("items", "Đơn giá sản phẩm ngoài danh mục phải lớn hơn 0.");
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
                    line.BannerMessage));
            }
        }

        return snapshots;
    }

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

    private static OrderDetails ToDetails(CreateOrderForm form, IReadOnlyCollection<OrderItemSnapshot> items)
    {
        var shippingFee = form.PickupAtShop ? 0 : form.ShippingFee;
        var orderValue = items.Sum(item => item.UnitPrice * item.Quantity - item.DiscountAmount) + shippingFee;
        var deposit = DefaultDepositCalculator.Resolve(orderValue, form.DepositAmount);
        return new(
        form.OrdererName,
        form.OrdererPhone ?? string.Empty,
        form.RecipientName,
        form.RecipientPhone,
        form.PickupAtShop,
        form.ProvinceShipping,
        form.DeliveryAddress,
        form.DeliveryAddressDescription,
        form.DeliveryLatitude,
        form.DeliveryLongitude,
        form.DeliveryAt.UtcDateTime,
        form.DeliveryTo?.UtcDateTime,
        deposit,
        form.ShippingFee,
        null,
        form.Description,
        form.ContentNote);
    }

    private static OrderDetails ToDetails(UpdateOrderForm form) => new(
        form.OrdererName,
        form.OrdererPhone ?? string.Empty,
        form.RecipientName,
        form.RecipientPhone,
        form.PickupAtShop,
        form.ProvinceShipping,
        form.DeliveryAddress,
        form.DeliveryAddressDescription,
        form.DeliveryLatitude,
        form.DeliveryLongitude,
        form.DeliveryAt.UtcDateTime,
        form.DeliveryTo?.UtcDateTime,
        form.DepositAmount,
        form.ShippingFee,
        form.ShippingFeeActual,
        form.Description,
        form.ContentNote);

    internal static OrderListItemDto ToListDto(Order order) => new(
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
            .FirstOrDefault());

    private static OrderDetailDto ToDetailDto(Order order) => new(
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
            order.Images.Where(image => image.OrderItemId == item.Id).OrderBy(image => image.SortOrder)
                .Select(image => new OrderImageDto(image.Id, image.OrderItemId, image.ImageUrl, image.SortOrder, image.Description)).ToList())).ToList(),
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
            log.Note)).ToList());

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

    private (Guid? Id, string? Name) CurrentActor()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var subject = principal?.FindFirstValue("sub") ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return (
            Guid.TryParse(subject, out var id) ? id : null,
            principal?.FindFirstValue("unique_name") ?? principal?.Identity?.Name);
    }

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

    private static void EnsureManualItemsHaveImages(Order order)
    {
        var illustratedItemIds = order.Images
            .Where(image => image.OrderItemId.HasValue)
            .Select(image => image.OrderItemId!.Value)
            .ToHashSet();
        if (order.Items.Any(item => !item.ProductId.HasValue && !illustratedItemIds.Contains(item.Id)))
            throw Validation("items", "Every item outside the product catalog must have at least one illustration image.");
    }

    private static TrackedOrderChildren CaptureTrackedChildren(Order order) => new(
        order.Items.Select(item => item.Id).ToHashSet(),
        order.Images.Select(image => image.Id).ToHashSet(),
        order.ChangeLogs.Select(log => log.Id).ToHashSet());

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
        HashSet<Guid> ChangeLogIds);
}
