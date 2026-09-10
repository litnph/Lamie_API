using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.Infrastructure.Persistence;

public sealed class ReferentialIntegrityService : IReferentialIntegrityService
{
    private readonly AppDbContext _context;

    public ReferentialIntegrityService(AppDbContext context)
    {
        _context = context;
    }

    public async Task ValidateProductReferencesAsync(
        ProductReferenceSet references,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();

        if (!await _context.Categories.AsNoTracking()
                .AnyAsync(item => item.Id == references.CategoryId, cancellationToken))
        {
            errors["categoryId"] = ["CategoryId does not exist"];
        }

        if (!await _context.ProductTypes.AsNoTracking()
                .AnyAsync(item => item.Id == references.ProductTypeId, cancellationToken))
        {
            errors["productTypeId"] = ["ProductTypeId does not exist"];
        }

        await AddMissingIdsAsync(
            references.TagIds,
            _context.Tags.Select(item => item.Id),
            "tagIds",
            "Tag",
            errors,
            cancellationToken);
        await AddMissingIdsAsync(
            references.ColorIds,
            _context.Colors.Select(item => item.Id),
            "colorIds",
            "Color",
            errors,
            cancellationToken);
        await AddMissingIdsAsync(
            references.CollectionIds,
            _context.Collections.Select(item => item.Id),
            "collectionIds",
            "Collection",
            errors,
            cancellationToken);
        await AddMissingIdsAsync(
            references.StyleIds,
            _context.Styles.Select(item => item.Id),
            "styleIds",
            "Style",
            errors,
            cancellationToken);
        await AddMissingIdsAsync(
            references.OccasionIds,
            _context.Occasions.Select(item => item.Id),
            "occasionIds",
            "Occasion",
            errors,
            cancellationToken);
        var recipeReferences = references.Ingredients ?? Array.Empty<ProductIngredientReference>();
        var recipeIngredientIds = recipeReferences.Select(item => item.IngredientId).Distinct().ToArray();
        var ingredientStates = await (
                from ingredient in _context.Ingredients.AsNoTracking()
                join unit in _context.MeasurementUnits.AsNoTracking()
                    on ingredient.BaseUnitId equals unit.Id
                where recipeIngredientIds.Contains(ingredient.Id)
                select new { ingredient.Id, ingredient.IsActive, UnitIsActive = unit.IsActive, unit.AllowsFractional })
            .ToListAsync(cancellationToken);
        var existingRecipes = (references.ExistingIngredients ?? Array.Empty<ProductIngredientReference>())
            .ToDictionary(item => item.IngredientId, item => item.BaseQuantity);
        var stateById = ingredientStates.ToDictionary(item => item.Id);
        var invalidIngredientIds = recipeReferences
            .Where(reference => !stateById.TryGetValue(reference.IngredientId, out var state)
                || (!(state.IsActive && state.UnitIsActive)
                    && (!existingRecipes.TryGetValue(reference.IngredientId, out var existingQuantity)
                        || existingQuantity != reference.BaseQuantity)))
            .Select(reference => reference.IngredientId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (invalidIngredientIds.Length > 0)
            errors["ingredients"] =
                [$"Unknown, inactive, or modified inactive ingredient ids: {string.Join(", ", invalidIngredientIds)}"];
        var wholeNumberIngredientIds = ingredientStates
            .Where(item => !item.AllowsFractional)
            .Select(item => item.Id)
            .ToHashSet();
        var invalidFractionalIds = recipeReferences
            .Where(item => wholeNumberIngredientIds.Contains(item.IngredientId)
                           && item.BaseQuantity != decimal.Truncate(item.BaseQuantity))
            .Select(item => item.IngredientId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (invalidFractionalIds.Length > 0)
            errors["ingredients"] =
                [$"These ingredients require whole-number quantities: {string.Join(", ", invalidFractionalIds)}"];

        var requestedLanguages = references.LanguageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedLanguages.Length > 0)
        {
            var existingLanguages = await _context.Languages.AsNoTracking()
                .Where(item => requestedLanguages.Contains(item.Code))
                .Select(item => item.Code)
                .ToListAsync(cancellationToken);
            var missingLanguages = requestedLanguages
                .Except(existingLanguages, StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingLanguages.Length > 0)
            {
                errors["translations"] =
                    [$"Unsupported language codes: {string.Join(", ", missingLanguages)}"];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    public async Task EnsureCanDeleteAsync(
        ReferencedMasterData target,
        object id,
        CancellationToken cancellationToken = default)
    {
        var inUse = target switch
        {
            ReferencedMasterData.Category =>
                await _context.Products.AsNoTracking()
                    .AnyAsync(item => item.CategoryId == RequireInt(id), cancellationToken),
            ReferencedMasterData.Collection =>
                await _context.ProductCollections.AsNoTracking()
                    .AnyAsync(item => item.CollectionId == RequireInt(id), cancellationToken),
            ReferencedMasterData.Color =>
                await _context.ProductColors.AsNoTracking()
                    .AnyAsync(item => item.ColorId == RequireInt(id), cancellationToken),
            ReferencedMasterData.Language =>
                await IsLanguageInUseAsync(RequireString(id), cancellationToken),
            ReferencedMasterData.Occasion =>
                await _context.ProductOccasions.AsNoTracking()
                    .AnyAsync(item => item.OccasionId == RequireInt(id), cancellationToken),
            ReferencedMasterData.ProductType =>
                await _context.Products.AsNoTracking()
                    .AnyAsync(item => item.ProductTypeId == RequireInt(id), cancellationToken),
            ReferencedMasterData.Style =>
                await _context.ProductStyles.AsNoTracking()
                    .AnyAsync(item => item.StyleId == RequireInt(id), cancellationToken),
            ReferencedMasterData.Tag =>
                await _context.ProductTags.AsNoTracking()
                    .AnyAsync(item => item.TagId == RequireInt(id), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };

        if (inUse)
        {
            throw new ConflictException(
                $"{target} '{id}' is in use and cannot be deleted. Deactivate it instead.");
        }
    }

    private async Task<bool> IsLanguageInUseAsync(
        string code,
        CancellationToken cancellationToken)
    {
        return await _context.ProductTranslations.AsNoTracking()
                   .AnyAsync(item => item.LanguageCode == code, cancellationToken)
               || await _context.ProductTypeTranslations.AsNoTracking()
                   .AnyAsync(item => item.LanguageCode == code, cancellationToken)
               || await _context.CategoryTranslations.AsNoTracking()
                   .AnyAsync(item => item.LanguageCode == code, cancellationToken)
               || await _context.CollectionTranslations.AsNoTracking()
                   .AnyAsync(item => item.LanguageCode == code, cancellationToken)
               || await _context.ColorTranslations.AsNoTracking()
                   .AnyAsync(item => item.LanguageCode == code, cancellationToken)
               || await _context.OccasionTranslations.AsNoTracking()
                   .AnyAsync(item => item.LanguageCode == code, cancellationToken)
               || await _context.StyleTranslations.AsNoTracking()
                   .AnyAsync(item => item.LanguageCode == code, cancellationToken)
               || await _context.TagTranslations.AsNoTracking()
                   .AnyAsync(item => item.LanguageCode == code, cancellationToken);
    }

    private static async Task AddMissingIdsAsync(
        IReadOnlyCollection<int> requestedIds,
        IQueryable<int> existingIdsQuery,
        string field,
        string entityName,
        IDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var distinctIds = requestedIds.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return;
        }

        var existingIds = await existingIdsQuery
            .Where(id => distinctIds.Contains(id))
            .ToListAsync(cancellationToken);
        var missingIds = distinctIds.Except(existingIds).OrderBy(id => id).ToArray();
        if (missingIds.Length > 0)
        {
            errors[field] =
                [$"Unknown {entityName} ids: {string.Join(", ", missingIds)}"];
        }
    }

    private static int RequireInt(object id) =>
        id is int value
            ? value
            : throw new ArgumentException("A numeric master-data id is required.", nameof(id));

    private static string RequireString(object id) =>
        id as string
        ?? throw new ArgumentException("A string master-data id is required.", nameof(id));
}
