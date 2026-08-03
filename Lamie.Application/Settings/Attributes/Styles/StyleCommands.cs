using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Styles;

// Commands
public sealed record CreateStyleCommand(bool IsActive, List<StyleTranslationInput> Translations) : IRequest<int>;

public sealed record UpdateStyleCommand(int Id, bool IsActive, List<StyleTranslationInput> Translations) : IRequest;

public sealed record DeleteStyleCommand(int Id) : IRequest;

// Command Handlers
public sealed class CreateStyleHandler : IRequestHandler<CreateStyleCommand, int>
{
    private readonly IStyleRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public CreateStyleHandler(IStyleRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task<int> Handle(CreateStyleCommand request, CancellationToken cancellationToken)
    {
        await StyleValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var style = new Style(request.IsActive);

        foreach (var t in request.Translations)
        {
            style.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.AddAsync(style);
        return style.Id;
    }
}

public sealed class UpdateStyleHandler : IRequestHandler<UpdateStyleCommand>
{
    private readonly IStyleRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public UpdateStyleHandler(IStyleRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task Handle(UpdateStyleCommand request, CancellationToken cancellationToken)
    {
        await StyleValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var style = await _repository.GetByIdAsync(request.Id);
        if (style is null)
        {
            throw new NotFoundException("Style", request.Id);
        }

        style.SetActive(request.IsActive);

        foreach (var t in request.Translations)
        {
            style.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.UpdateAsync(style);
    }
}

public sealed class DeleteStyleHandler : IRequestHandler<DeleteStyleCommand>
{
    private readonly IStyleRepository _repository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public DeleteStyleHandler(
        IStyleRepository repository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task Handle(DeleteStyleCommand request, CancellationToken cancellationToken)
    {
        var style = await _repository.GetByIdAsync(request.Id);
        if (style is null)
        {
            throw new NotFoundException("Style", request.Id);
        }

        await _referentialIntegrity.EnsureCanDeleteAsync(
            ReferencedMasterData.Style,
            request.Id,
            cancellationToken);
        await _repository.DeleteAsync(style);
    }
}

internal static class StyleValidation
{
    public static async Task ValidateTranslationsAsync(
        IReadOnlyList<StyleTranslationInput> translations,
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

