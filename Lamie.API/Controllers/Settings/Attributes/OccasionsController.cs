using Lamie.Application.Settings.Attributes.Occasions;
using Lamie.Application.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.SettingsView)]
[Route("api/settings/attributes/occasions")]
public sealed class OccasionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OccasionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetOccasions(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllOccasionsQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOccasionById(int id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOccasionByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> CreateOccasion([FromBody] CreateOccasionCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetOccasionById), new { id }, new { id });
    }

    [HttpPut]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> UpdateOccasion([FromBody] UpdateOccasionCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> DeleteOccasion(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteOccasionCommand(id), cancellationToken);
        return NoContent();
    }
}

