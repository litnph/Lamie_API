using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using Lamie.Application.Common.Uploads;

namespace Lamie.Application.Settings.Products.Commands
{
    public class CreateProductValidator : AbstractValidator<CreateProductCommand>
    {
        public CreateProductValidator()
        {
            RuleFor(x => x.Sku)
                .Must(sku => string.IsNullOrWhiteSpace(sku) || Lamie.Domain.Products.ProductSku.IsValidNewSku(Lamie.Domain.Products.ProductSku.Normalize(sku)))
                .WithMessage("SKU must be exactly four uppercase letters or digits when supplied.");

            RuleFor(x => x.Price)
                .GreaterThan(0);

            RuleFor(x => x.SalePrice)
                .GreaterThan(0)
                .LessThan(x => x.Price)
                .When(x => x.SalePrice.HasValue);

            RuleFor(x => x.Stock)
                .GreaterThanOrEqualTo(0);

            RuleFor(x => x.CategoryId)
                .GreaterThan(0);

            RuleFor(x => x.ProductTypeId)
                .GreaterThan(0);

            RuleFor(x => x.ThumbnailFile)
                .Must(ImageUploadPolicy.HasAllowedMetadata)
                .WithMessage("ThumbnailFile must be a JPG, PNG, WEBP, or GIF image no larger than 10 MB.")
                .MustAsync(ImageUploadPolicy.HasValidSignatureAsync)
                .WithMessage("ThumbnailFile content does not match its image type.");

            RuleFor(x => x.Translations)
                .NotEmpty()
                .Must(HaveUniqueLanguageCodes)
                .WithMessage("Translation language codes must be unique.");

            RuleForEach(x => x.Translations)
                .ChildRules(t =>
                {
                    t.RuleFor(x => x.LanguageCode).NotEmpty();
                    t.RuleFor(x => x.Name).NotEmpty();
                    t.RuleFor(x => x.Slug).NotEmpty();
                });

            RuleFor(x => x.Images)
                .Must(images => images.Count <= ImageUploadPolicy.MaximumFileCount)
                .WithMessage($"At most {ImageUploadPolicy.MaximumFileCount} product images are allowed.");

            RuleForEach(x => x.Images)
                .ChildRules(i =>
                {
                    i.RuleFor(x => x)
                        .Must(img =>
                            !string.IsNullOrWhiteSpace(img.ImageUrl) ||
                            (img.ImageFile is { Length: > 0 }))
                        .WithMessage("Either ImageUrl or ImageFile is required for each image.");
                    i.RuleFor(x => x.ImageFile)
                        .Must(ImageUploadPolicy.HasAllowedMetadata)
                        .WithMessage("ImageFile must be a JPG, PNG, WEBP, or GIF image no larger than 10 MB.")
                        .MustAsync(ImageUploadPolicy.HasValidSignatureAsync)
                        .WithMessage("ImageFile content does not match its image type.");
                });

            RuleFor(x => x.Ingredients)
                .Must(HaveUniqueIngredientIds)
                .WithMessage("Product ingredient ids must be unique.");
            RuleForEach(x => x.Ingredients).ChildRules(ingredient =>
            {
                ingredient.RuleFor(x => x.IngredientId).GreaterThan(0);
                ingredient.RuleFor(x => x.BaseQuantity).GreaterThan(0).PrecisionScale(18, 6, true);
                ingredient.RuleFor(x => x.Note).MaximumLength(1000);
                ingredient.RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
            });

            RuleFor(x => x.SimilarProductIds)
                .Must(ids => ids.All(id => id > 0) && ids.Distinct().Count() == ids.Count)
                .WithMessage("Similar product ids must be unique positive values.");
        }

        private static bool HaveUniqueLanguageCodes(
            IReadOnlyCollection<Lamie.Application.Settings.Products.Dtos.CreateProductTranslationDto> translations)
        {
            return translations
                .Select(translation => translation.LanguageCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == translations.Count;
        }

        private static bool HaveUniqueIngredientIds(
            IReadOnlyCollection<Lamie.Application.Settings.Products.Dtos.ProductIngredientInputDto> ingredients) =>
            ingredients.Select(item => item.IngredientId).Distinct().Count() == ingredients.Count;
    }
}
