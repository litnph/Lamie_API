using Lamie.Application.Channels;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers.Settings.Attributes;

[ApiController]
[Authorize(Policy = PermissionNames.ChannelsView)]
[Route("api/settings/attributes/channels")]
public sealed class ChannelsController : ControllerBase
{
    private readonly IChannelService _channelService;

    public ChannelsController(IChannelService channelService)
    {
        _channelService = channelService;
    }

    [HttpGet]
    public Task<IReadOnlyList<ChannelDto>> List(CancellationToken cancellationToken) =>
        _channelService.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<ChannelDto> Get(Guid id, CancellationToken cancellationToken) =>
        _channelService.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.ChannelsManage)]
    public async Task<IActionResult> Create(CreateChannelRequest request, CancellationToken cancellationToken)
    {
        var id = await _channelService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut]
    [Authorize(Policy = PermissionNames.ChannelsManage)]
    public async Task<IActionResult> Update(UpdateChannelRequest request, CancellationToken cancellationToken)
    {
        await _channelService.UpdateAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _channelService.DeleteOrDisableAsync(id, cancellationToken);
        return NoContent();
    }
}
