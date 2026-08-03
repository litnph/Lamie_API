using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.ProductTypes;

public sealed record CreateProductTypeCommand(
    string Code,
    int SortOrder,
    bool IsActive,
    List<ProductTypeTranslationInput> Translations) : IRequest<int>;

public sealed record UpdateProductTypeCommand(
    int Id,
    string Code,
    int SortOrder,
    bool IsActive,
    List<ProductTypeTranslationInput> Translations) : IRequest;

public sealed record DeleteProductTypeCommand(int Id) : IRequest;

public sealed class CreateProductTypeHandler : IRequestHandler<CreateProductTypeCommand, int>
{
    private readonly IProductTypeRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public CreateProductTypeHandler(IProductTypeRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task<int> Handle(CreateProductTypeCommand request, CancellationToken cancellationToken)
    {
        await ProductTypeValidation.ValidateAsync(
            request.Code, request.SortOrder, request.Translations, _repository, _languageRepository, null, cancellationToken);

        var productType = new ProductType(request.Code, request.SortOrder, request.IsActive);
        foreach (var translation in request.Translations)
            productType.AddOrUpdateTranslation(translation.LanguageCode, translation.Name, translation.Description);

        await _repository.AddAsync(productType);
        return productType.Id;
    }
}

public sealed class UpdateProductTypeHandler : IRequestHandler<UpdateProductTypeCommand>
{
    private readonly IProductTypeRepository _repository;
    private readonly ILanguageRepository _languageRepository;

    public UpdateProductTypeHandler(IProductTypeRepository repository, ILanguageRepository languageRepository)
    {
        _repository = repository;
        _languageRepository = languageRepository;
    }

    public async Task Handle(UpdateProductTypeCommand request, CancellationToken cancellationToken)
    {
        await ProductTypeValidation.ValidateAsync(
            request.Code, request.SortOrder, request.Translations, _repository, _languageRepository, request.Id, cancellationToken);

        var productType = await _repository.GetByIdAsync(request.Id)
            ?? throw new NotFoundException("ProductType", request.Id);
        productType.Update(request.Code, request.SortOrder, request.IsActive);
        foreach (var translation in request.Translations)
            productType.AddOrUpdateTranslation(translation.LanguageCode, translation.Name, translation.Description);

        await _repository.UpdateAsync(productType);
    }
}

public sealed class DeleteProductTypeHandler : IRequestHandler<DeleteProductTypeCommand>
{
    private readonly IProductTypeRepository _repository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public DeleteProductTypeHandler(
        IProductTypeRepository repository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task Handle(DeleteProductTypeCommand request, CancellationToken cancellationToken)
    {
        var productType = await _repository.GetByIdAsync(request.Id)
            ?? throw new NotFoundException("ProductType", request.Id);
        await _referentialIntegrity.EnsureCanDeleteAsync(
            ReferencedMasterData.ProductType,
            request.Id,
            cancellationToken);
        await _repository.DeleteAsync(productType);
    }
}

internal static class ProductTypeValidation
{
    public static async Task ValidateAsync(
        string code,
        int sortOrder,
        IReadOnlyList<ProductTypeTranslationInput>? translations,
        IProductTypeRepository repository,
        ILanguageRepository languageRepository,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedCode.Length == 0)
            errors["code"] = ["Code is required"];
        else if (normalizedCode.Length > 50)
            errors["code"] = ["Code must not exceed 50 characters"];
        else if (normalizedCode.Any(character =>
                     !char.IsAsciiLetterUpper(character) && !char.IsDigit(character) && character != '_'))
            errors["code"] = ["Code may contain only A-Z, 0-9, and underscore"];
        else if (await repository.CodeExistsAsync(normalizedCode, excludingId))
            errors["code"] = ["Code already exists"];

        if (sortOrder < 0)
            errors["sortOrder"] = ["SortOrder must be greater than or equal to 0"];
        if (translations is null || translations.Count == 0)
            errors["translations"] = ["At least one translation is required"];
        else
        {
            var duplicateLanguages = translations
                .Where(item => !string.IsNullOrWhiteSpace(item.LanguageCode))
                .GroupBy(item => item.LanguageCode, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < translations.Count; index++)
            {
                var translation = translations[index];
                if (string.IsNullOrWhiteSpace(translation.LanguageCode))
                    errors[$"translations[{index}].languageCode"] = ["LanguageCode is required"];
                else if (duplicateLanguages.Contains(translation.LanguageCode))
                    errors[$"translations[{index}].languageCode"] = ["LanguageCode must be unique"];
                else if (!await languageRepository.ExistsAsync(translation.LanguageCode, cancellationToken))
                    errors[$"translations[{index}].languageCode"] = ["LanguageCode is not supported"];

                if (string.IsNullOrWhiteSpace(translation.Name))
                    errors[$"translations[{index}].name"] = ["Name is required"];
            }
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
