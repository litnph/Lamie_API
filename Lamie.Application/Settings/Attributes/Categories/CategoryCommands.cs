using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Categories;

// Commands
public sealed record CreateCategoryCommand(int SortOrder, bool IsActive, List<CategoryTranslationInput> Translations) : IRequest<int>;

public sealed record UpdateCategoryCommand(int Id, int SortOrder, bool IsActive, List<CategoryTranslationInput> Translations) : IRequest;

public sealed record DeleteCategoryCommand(int Id) : IRequest;

// Command Handlers
public sealed class CreateCategoryHandler : IRequestHandler<CreateCategoryCommand, int>
{
    private readonly ICategoryRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public CreateCategoryHandler(ICategoryRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task<int> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        await CategoryValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var category = new Category(request.SortOrder, request.IsActive);

        foreach (var t in request.Translations)
        {
            category.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.AddAsync(category);
        return category.Id;
    }
}

public sealed class UpdateCategoryHandler : IRequestHandler<UpdateCategoryCommand>
{
    private readonly ICategoryRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public UpdateCategoryHandler(ICategoryRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        await CategoryValidation.ValidateTranslationsAsync(request.Translations, _languageRepository, cancellationToken);

        var category = await _repository.GetByIdAsync(request.Id);
        if (category is null)
        {
            throw new NotFoundException("Category", request.Id);
        }

        category.Update(request.SortOrder, request.IsActive);

        foreach (var t in request.Translations)
        {
            category.AddOrUpdateTranslation(t.LanguageCode, t.Name, t.Description);
        }

        await _repository.UpdateAsync(category);
    }
}

public sealed class DeleteCategoryHandler : IRequestHandler<DeleteCategoryCommand>
{
    private readonly ICategoryRepository _repository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public DeleteCategoryHandler(
        ICategoryRepository repository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _repository.GetByIdAsync(request.Id);
        if (category is null)
        {
            throw new NotFoundException("Category", request.Id);
        }

        await _referentialIntegrity.EnsureCanDeleteAsync(
            ReferencedMasterData.Category,
            request.Id,
            cancellationToken);
        await _repository.DeleteAsync(category);
    }
}

internal static class CategoryValidation
{
    public static async Task ValidateTranslationsAsync(
        IReadOnlyList<CategoryTranslationInput> translations,
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

