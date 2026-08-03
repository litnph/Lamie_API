namespace Lamie.Application.Channels;

public sealed record ChannelDto(
    Guid Id,
    string Code,
    string Name,
    string? IconUrl,
    bool IsActive,
    int SortOrder);

public sealed record CreateChannelRequest(
    string Code,
    string Name,
    string? IconUrl,
    int SortOrder,
    bool IsActive);

public sealed record UpdateChannelRequest(
    Guid Id,
    string Name,
    string? IconUrl,
    int SortOrder,
    bool IsActive);

public interface IChannelService
{
    Task<IReadOnlyList<ChannelDto>> ListAsync(CancellationToken cancellationToken);
    Task<ChannelDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(CreateChannelRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(UpdateChannelRequest request, CancellationToken cancellationToken);
    Task DeleteOrDisableAsync(Guid id, CancellationToken cancellationToken);
}
