using Lamie.Application.Settings.Attributes.Collections;
using Lamie.Application.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.SettingsView)]
[Route("api/settings/attributes/collections")]
public sealed class CollectionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CollectionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetCollections(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllCollectionsQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCollectionById(int id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCollectionByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> CreateCollection([FromBody] CreateCollectionCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetCollectionById), new { id }, new { id });
    }

    [HttpPut]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> UpdateCollection([FromBody] UpdateCollectionCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> DeleteCollection(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteCollectionCommand(id), cancellationToken);
        return NoContent();
    }
}

