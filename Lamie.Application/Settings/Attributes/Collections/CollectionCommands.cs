using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Collections;

// Commands
public sealed record CreateCollectionCommand(bool IsActive, List<CollectionTranslationInput> Translations) : IRequest<int>;

public sealed record UpdateCollectionCommand(int Id, bool IsActive, List<CollectionTranslationInput> Translations) : IRequest;

public sealed record DeleteCollectionCommand(int Id) : IRequest;

// Command Handlers
public sealed class CreateCollectionHandler : IRequestHandler<CreateCollectionCommand, int>
{
    private readonly ICollectionRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public CreateCollectionHandler(ICollectionRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task<int> Handle(CreateCollectionCommand request, CancellationToken cancellationToken)
    {
        await CollectionValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var collection = new Collection(request.IsActive);

        foreach (var t in request.Translations)
        {
            collection.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.AddAsync(collection);
        return collection.Id;
    }
}

public sealed class UpdateCollectionHandler : IRequestHandler<UpdateCollectionCommand>
{
    private readonly ICollectionRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public UpdateCollectionHandler(ICollectionRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task Handle(UpdateCollectionCommand request, CancellationToken cancellationToken)
    {
        await CollectionValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var collection = await _repository.GetByIdAsync(request.Id);
        if (collection is null)
        {
            throw new NotFoundException("Collection", request.Id);
        }

        collection.SetActive(request.IsActive);

        foreach (var t in request.Translations)
        {
            collection.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.UpdateAsync(collection);
    }
}

public sealed class DeleteCollectionHandler : IRequestHandler<DeleteCollectionCommand>
{
    private readonly ICollectionRepository _repository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public DeleteCollectionHandler(
        ICollectionRepository repository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task Handle(DeleteCollectionCommand request, CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(request.Id);
        if (collection is null)
        {
            throw new NotFoundException("Collection", request.Id);
        }

        await _referentialIntegrity.EnsureCanDeleteAsync(
            ReferencedMasterData.Collection,
            request.Id,
            cancellationToken);
        await _repository.DeleteAsync(collection);
    }
}

internal static class CollectionValidation
{
    public static async Task ValidateTranslationsAsync(
        IReadOnlyList<CollectionTranslationInput> translations,
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

