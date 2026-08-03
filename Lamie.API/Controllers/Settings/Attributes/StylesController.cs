using Lamie.Application.Settings.Attributes.Styles;
using Lamie.Application.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.SettingsView)]
[Route("api/settings/attributes/styles")]
public sealed class StylesController : ControllerBase
{
    private readonly IMediator _mediator;

    public StylesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetStyles(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllStylesQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetStyleById(int id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStyleByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> CreateStyle([FromBody] CreateStyleCommand command, CancellationToken cancellationToken)
    {
        var id = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetStyleById), new { id }, new { id });
    }

    [HttpPut]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> UpdateStyle([FromBody] UpdateStyleCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> DeleteStyle(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteStyleCommand(id), cancellationToken);
        return NoContent();
    }
}

