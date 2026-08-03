using Lamie.Application.Settings.Attributes.Languages;
using Lamie.Application.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.SettingsView)]
[Route("api/settings/attributes/languages")]
public sealed class LanguagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public LanguagesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetLanguages(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAllLanguagesQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> GetLanguageByCode(string code, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLanguageByCodeQuery(code), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> CreateLanguage([FromBody] CreateLanguageCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetLanguageByCode), new { code = command.Code }, new { code = command.Code });
    }

    [HttpPut]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> UpdateLanguage([FromBody] UpdateLanguageCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{code}")]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    public async Task<IActionResult> DeleteLanguage(string code, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteLanguageCommand(code), cancellationToken);
        return NoContent();
    }
}

