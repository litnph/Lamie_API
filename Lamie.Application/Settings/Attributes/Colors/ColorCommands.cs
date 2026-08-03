using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Colors;

// Commands
public sealed record CreateColorCommand(string HexCode, string RgbCode, bool IsActive, List<ColorTranslationInput> Translations) : IRequest<int>;

public sealed record UpdateColorCommand(int Id, string HexCode, string RgbCode, bool IsActive, List<ColorTranslationInput> Translations) : IRequest;

public sealed record DeleteColorCommand(int Id) : IRequest;

// Command Handlers
public sealed class CreateColorHandler : IRequestHandler<CreateColorCommand, int>
{
    private readonly IColorRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public CreateColorHandler(IColorRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task<int> Handle(CreateColorCommand request, CancellationToken cancellationToken)
    {
        ColorValidation.ValidateColorCodes(request.HexCode, request.RgbCode);
        await ColorValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var color = new Color(request.HexCode, request.RgbCode, request.IsActive);

        foreach (var t in request.Translations)
        {
            color.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.AddAsync(color);
        return color.Id;
    }
}

public sealed class UpdateColorHandler : IRequestHandler<UpdateColorCommand>
{
    private readonly IColorRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public UpdateColorHandler(IColorRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task Handle(UpdateColorCommand request, CancellationToken cancellationToken)
    {
        ColorValidation.ValidateColorCodes(request.HexCode, request.RgbCode);
        await ColorValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var color = await _repository.GetByIdAsync(request.Id);
        if (color is null)
        {
            throw new NotFoundException("Color", request.Id);
        }

        color.Update(request.HexCode, request.RgbCode, request.IsActive);

        foreach (var t in request.Translations)
        {
            color.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.UpdateAsync(color);
    }
}

public sealed class DeleteColorHandler : IRequestHandler<DeleteColorCommand>
{
    private readonly IColorRepository _repository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public DeleteColorHandler(
        IColorRepository repository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task Handle(DeleteColorCommand request, CancellationToken cancellationToken)
    {
        var color = await _repository.GetByIdAsync(request.Id);
        if (color is null)
        {
            throw new NotFoundException("Color", request.Id);
        }

        await _referentialIntegrity.EnsureCanDeleteAsync(
            ReferencedMasterData.Color,
            request.Id,
            cancellationToken);
        await _repository.DeleteAsync(color);
    }
}

internal static class ColorValidation
{
    public static void ValidateColorCodes(string hexCode, string rgbCode)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(hexCode))
        {
            errors["hexCode"] = ["HexCode is required"];
        }

        if (string.IsNullOrWhiteSpace(rgbCode))
        {
            errors["rgbCode"] = ["RgbCode is required"];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    public static async Task ValidateTranslationsAsync(
        IReadOnlyList<ColorTranslationInput> translations,
        ILanguageRepository languageRepository,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (translations is null || translations.Count == 0)
        {
            errors["translations"] = ["At least one translation is required"];
            throw new ValidationException(errors);
        }

        for (var i = 0; i < translations.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(translations[i].LanguageCode))
            {
                errors[$"translations[{i}].languageCode"] = ["LanguageCode is required"];
            }

            if (string.IsNullOrWhiteSpace(translations[i].Name))
            {
                errors[$"translations[{i}].name"] = ["Name is required"];
            }

            if (!string.IsNullOrWhiteSpace(translations[i].LanguageCode))
            {
                var exists = await languageRepository.ExistsAsync(
                    translations[i].LanguageCode,
                    cancellationToken);

                if (!exists)
                {
                    errors[$"translations[{i}].languageCode"] = ["LanguageCode is not supported"];
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}

