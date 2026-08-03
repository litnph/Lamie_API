using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.MasterData.Tags;

// Commands
public sealed record CreateTagCommand(bool IsActive, List<TagTranslationInput> Translations) : IRequest<int>;

public sealed record UpdateTagCommand(int Id, bool IsActive, List<TagTranslationInput> Translations) : IRequest;

public sealed record DeleteTagCommand(int Id) : IRequest;

// Command Handlers
public sealed class CreateTagHandler : IRequestHandler<CreateTagCommand, int>
{
    private readonly ITagRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public CreateTagHandler(ITagRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task<int> Handle(CreateTagCommand request, CancellationToken cancellationToken)
    {
        await TagValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var tag = new Tag(request.IsActive);

        foreach (var t in request.Translations)
        {
            tag.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.AddAsync(tag);
        return tag.Id;
    }
}

public sealed class UpdateTagHandler : IRequestHandler<UpdateTagCommand>
{
    private readonly ITagRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public UpdateTagHandler(ITagRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task Handle(UpdateTagCommand request, CancellationToken cancellationToken)
    {
        await TagValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var tag = await _repository.GetByIdAsync(request.Id);
        if (tag is null)
        {
            throw new NotFoundException("Tag", request.Id);
        }

        tag.SetActive(request.IsActive);

        foreach (var t in request.Translations)
        {
            tag.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.UpdateAsync(tag);
    }
}

public sealed class DeleteTagHandler : IRequestHandler<DeleteTagCommand>
{
    private readonly ITagRepository _repository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public DeleteTagHandler(
        ITagRepository repository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task Handle(DeleteTagCommand request, CancellationToken cancellationToken)
    {
        var tag = await _repository.GetByIdAsync(request.Id);
        if (tag is null)
        {
            throw new NotFoundException("Tag", request.Id);
        }

        await _referentialIntegrity.EnsureCanDeleteAsync(
            ReferencedMasterData.Tag,
            request.Id,
            cancellationToken);
        await _repository.DeleteAsync(tag);
    }
}

internal static class TagValidation
{
    public static async Task ValidateTranslationsAsync(
        IReadOnlyList<TagTranslationInput> translations,
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

