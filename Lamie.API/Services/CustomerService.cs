using Lamie.Application.Customers;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class CustomerService : ICustomerService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public CustomerService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<PagedCustomersDto> ListAsync(
        CustomerListQuery query,
        CancellationToken cancellationToken)
    {
        ValidateListQuery(query);
        var customers = _dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.IsActive &&
                _dbContext.Orders.Any(order => order.CustomerId == customer.Id));

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var normalizedPhone = DigitsOnly(search);
            customers = customers.Where(customer =>
                customer.Name.Contains(search) ||
                customer.Phone.Contains(search) ||
                (customer.Email != null && customer.Email.Contains(search)) ||
                (normalizedPhone.Length > 0 && customer.NormalizedPhone.Contains(normalizedPhone)));
        }

        if (query.OrderStatus.HasValue)
        {
            var status = query.OrderStatus.Value;
            customers = customers.Where(customer =>
                _dbContext.Orders.Any(order => order.CustomerId == customer.Id && order.OrderStatus == status));
        }

        var totalCount = await customers.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)query.PageSize));
        var page = Math.Min(query.Page, totalPages);
        var rows = await customers
            .Select(customer => new
            {
                Customer = customer,
                OrderCount = _dbContext.Orders.Count(order => order.CustomerId == customer.Id),
                PaidOrderCount = _dbContext.Orders.Count(order =>
                    order.CustomerId == customer.Id &&
                    order.PaymentStatus == PaymentStatus.Paid &&
                    order.OrderStatus != OrderStatus.Cancelled),
                TotalSpent = _dbContext.Orders
                    .Where(order =>
                        order.CustomerId == customer.Id &&
                        order.PaymentStatus == PaymentStatus.Paid &&
                        order.OrderStatus != OrderStatus.Cancelled)
                    .Sum(order => (decimal?)order.TotalAmount) ?? 0,
                LastPurchaseAt = _dbContext.Orders
                    .Where(order =>
                        order.CustomerId == customer.Id &&
                        order.PaymentStatus == PaymentStatus.Paid &&
                        order.OrderStatus != OrderStatus.Cancelled)
                    .Max(order => (DateTime?)order.CreatedAt),
                LatestOrderAt = _dbContext.Orders
                    .Where(order => order.CustomerId == customer.Id)
                    .Max(order => order.CreatedAt)
            })
            .OrderByDescending(row => row.LatestOrderAt)
            .ThenBy(row => row.Customer.Id)
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var customerIds = rows.Select(row => row.Customer.Id).ToList();
        var latestOrderIds = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId.HasValue && customerIds.Contains(order.CustomerId.Value))
            .GroupBy(order => order.CustomerId)
            .Select(group => group
                .OrderByDescending(order => order.CreatedAt)
                .ThenByDescending(order => order.Id)
                .Select(order => order.Id)
                .First())
            .ToListAsync(cancellationToken);
        var latestOrders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => latestOrderIds.Contains(order.Id))
            .ToDictionaryAsync(order => order.CustomerId!.Value, cancellationToken);

        var items = rows.Select(row =>
        {
            var latestOrder = latestOrders[row.Customer.Id];
            return new CustomerSummaryDto(
                row.Customer.Id,
                row.Customer.Name,
                row.Customer.Phone,
                row.OrderCount,
                row.PaidOrderCount,
                row.TotalSpent,
                OrderService.ToListDto(latestOrder),
                ToOffset(row.LastPurchaseAt));
        }).ToList();

        return new PagedCustomersDto(
            items,
            totalCount,
            page,
            query.PageSize,
            totalPages,
            page < totalPages,
            page > 1);
    }

    public async Task<CustomerDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), id);
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == id)
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
            throw new NotFoundException(nameof(Customer), id);

        return ToDetail(customer, orders);
    }

    public async Task<CustomerOrderNotesResultDto> GetOrderNotesAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Id == id && customer.IsActive, cancellationToken);
        if (!exists)
            throw new NotFoundException(nameof(Customer), id);

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.CustomerId == id &&
                (order.ContentNote != null || order.Description != null))
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new
            {
                order.Id,
                order.OrderCode,
                order.CreatedAt,
                order.ContentNote,
                order.Description
            })
            .ToListAsync(cancellationToken);

        var notes = new List<CustomerOrderNoteDto>();
        foreach (var order in orders)
        {
            var contentNote = order.ContentNote?.Trim();
            var description = order.Description?.Trim();
            if (!string.IsNullOrEmpty(contentNote))
            {
                notes.Add(new CustomerOrderNoteDto(
                    $"{order.Id}-content",
                    order.Id,
                    order.OrderCode,
                    ToOffset(order.CreatedAt),
                    "Nội dung đơn",
                    contentNote));
            }
            if (!string.IsNullOrEmpty(description) && description != contentNote)
            {
                notes.Add(new CustomerOrderNoteDto(
                    $"{order.Id}-description",
                    order.Id,
                    order.OrderCode,
                    ToOffset(order.CreatedAt),
                    "Mô tả đơn",
                    description));
            }
        }

        return new CustomerOrderNotesResultDto(notes, 0);
    }

    public async Task<CustomerDetailDto> UpdateNotesAsync(
        Guid id,
        UpdateCustomerNotesRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await _dbContext.Customers
            .SingleOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(Customer), id);
        customer.Update(
            customer.Name,
            customer.Phone,
            customer.Email,
            request.Notes,
            customer.IsActive,
            _timeProvider.GetUtcNow().UtcDateTime);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public static bool CountsAsSpending(PaymentStatus paymentStatus, OrderStatus orderStatus) =>
        paymentStatus == PaymentStatus.Paid && orderStatus != OrderStatus.Cancelled;

    private static CustomerDetailDto ToDetail(Customer customer, IReadOnlyList<Order> orders)
    {
        var paidOrders = orders.Where(order => CountsAsSpending(order.PaymentStatus, order.OrderStatus)).ToList();
        var orderDtos = orders.Select(OrderService.ToListDto).ToList();
        var addresses = orders
            .Where(order => !order.PickupAtShop && !string.IsNullOrWhiteSpace(order.DeliveryAddress))
            .GroupBy(order => order.DeliveryAddress!.Trim(), StringComparer.Create(new System.Globalization.CultureInfo("vi-VN"), true))
            .Select(group => group.First())
            .Select(order => new CustomerAddressDto(
                order.DeliveryAddress!.Trim(),
                order.Id,
                order.OrderCode,
                ToOffset(order.DeliveryAt)))
            .ToList();

        return new CustomerDetailDto(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.Notes,
            orders.Count,
            paidOrders.Count,
            paidOrders.Sum(order => order.TotalAmount),
            orderDtos[0],
            paidOrders.Count == 0 ? null : ToOffset(paidOrders[0].CreatedAt),
            orderDtos,
            addresses);
    }

    private static void ValidateListQuery(CustomerListQuery query)
    {
        if (query.Page < 1)
            throw Validation(nameof(query.Page), "Page must be at least 1.");
        if (query.PageSize is < 1 or > 100)
            throw Validation(nameof(query.PageSize), "Page size must be between 1 and 100.");
        if (query.Search?.Trim().Length > 200)
            throw Validation(nameof(query.Search), "Search cannot exceed 200 characters.");
        if (query.OrderStatus.HasValue && !Enum.IsDefined(query.OrderStatus.Value))
            throw Validation(nameof(query.OrderStatus), "Order status is invalid.");
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? ToOffset(value.Value) : null;

    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
