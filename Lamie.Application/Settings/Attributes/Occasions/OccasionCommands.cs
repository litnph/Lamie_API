using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Occasions;

// Commands
public sealed record CreateOccasionCommand(bool IsActive, List<OccasionTranslationInput> Translations) : IRequest<int>;

public sealed record UpdateOccasionCommand(int Id, bool IsActive, List<OccasionTranslationInput> Translations) : IRequest;

public sealed record DeleteOccasionCommand(int Id) : IRequest;

// Command Handlers
public sealed class CreateOccasionHandler : IRequestHandler<CreateOccasionCommand, int>
{
    private readonly IOccasionRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public CreateOccasionHandler(IOccasionRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task<int> Handle(CreateOccasionCommand request, CancellationToken cancellationToken)
    {
        await OccasionValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var occasion = new Occasion(request.IsActive);

        foreach (var t in request.Translations)
        {
            occasion.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.AddAsync(occasion);
        return occasion.Id;
    }
}

public sealed class UpdateOccasionHandler : IRequestHandler<UpdateOccasionCommand>
{
    private readonly IOccasionRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public UpdateOccasionHandler(IOccasionRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task Handle(UpdateOccasionCommand request, CancellationToken cancellationToken)
    {
        await OccasionValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var occasion = await _repository.GetByIdAsync(request.Id);
        if (occasion is null)
        {
            throw new NotFoundException("Occasion", request.Id);
        }

        occasion.SetActive(request.IsActive);

        foreach (var t in request.Translations)
        {
            occasion.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.UpdateAsync(occasion);
    }
}

public sealed class DeleteOccasionHandler : IRequestHandler<DeleteOccasionCommand>
{
    private readonly IOccasionRepository _repository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public DeleteOccasionHandler(
        IOccasionRepository repository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task Handle(DeleteOccasionCommand request, CancellationToken cancellationToken)
    {
        var occasion = await _repository.GetByIdAsync(request.Id);
        if (occasion is null)
        {
            throw new NotFoundException("Occasion", request.Id);
        }

        await _referentialIntegrity.EnsureCanDeleteAsync(
            ReferencedMasterData.Occasion,
            request.Id,
            cancellationToken);
        await _repository.DeleteAsync(occasion);
    }
}

internal static class OccasionValidation
{
    public static async Task ValidateTranslationsAsync(
        IReadOnlyList<OccasionTranslationInput> translations,
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

