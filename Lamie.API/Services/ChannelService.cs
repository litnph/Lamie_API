using Lamie.Application.Channels;
using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class ChannelService : IChannelService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ChannelService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ChannelDto>> ListAsync(CancellationToken cancellationToken) =>
        await _dbContext.Channels
            .AsNoTracking()
            .OrderBy(channel => channel.SortOrder)
            .ThenBy(channel => channel.Name)
            .Select(channel => new ChannelDto(
                channel.Id,
                channel.Code,
                channel.Name,
                channel.IconUrl,
                channel.IsActive,
                channel.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<ChannelDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var channel = await _dbContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Channel), id);

        return ToDto(channel);
    }

    public async Task<Guid> CreateAsync(CreateChannelRequest request, CancellationToken cancellationToken)
    {
        var normalizedCode = Channel.NormalizeCode(request.Code);
        if (await _dbContext.Channels.AnyAsync(channel => channel.Code == normalizedCode, cancellationToken))
            throw new ConflictException($"Channel code '{normalizedCode}' already exists.");

        var channel = new Channel(
            normalizedCode,
            request.Name,
            request.IconUrl,
            request.SortOrder,
            request.IsActive,
            UtcNow());
        _dbContext.Channels.Add(channel);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException($"Channel code '{normalizedCode}' already exists.");
        }

        return channel.Id;
    }

    public async Task UpdateAsync(UpdateChannelRequest request, CancellationToken cancellationToken)
    {
        var channel = await _dbContext.Channels.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Channel), request.Id);

        channel.Update(request.Name, request.IconUrl, request.SortOrder, request.IsActive, UtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteOrDisableAsync(Guid id, CancellationToken cancellationToken)
    {
        var channel = await _dbContext.Channels.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(Channel), id);

        if (channel.IsDefault)
        {
            channel.Disable(UtcNow());
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (await _dbContext.Orders.AnyAsync(order => order.ChannelId == id, cancellationToken))
        {
            channel.Disable(UtcNow());
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        _dbContext.Channels.Remove(channel);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            channel = await _dbContext.Channels.FindAsync([id], cancellationToken)
                ?? throw new NotFoundException(nameof(Channel), id);
            channel.Disable(UtcNow());
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static ChannelDto ToDto(Channel channel) => new(
        channel.Id,
        channel.Code,
        channel.Name,
        channel.IconUrl,
        channel.IsActive,
        channel.SortOrder);
}
