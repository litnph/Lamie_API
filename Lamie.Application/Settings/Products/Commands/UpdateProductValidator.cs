using FluentValidation;
using Lamie.Application.Common.Uploads;

namespace Lamie.Application.Settings.Products.Commands
{
    public class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
    {
        public UpdateProductValidator()
        {
            RuleFor(x => x.Id)
                .GreaterThan(0);

            RuleFor(x => x.Sku)
                .NotEmpty()
                .MaximumLength(50);

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
                .WithMessage("Translation language codes must be unique.")
                .When(x => x.IsFullReplacement);

            RuleForEach(x => x.Translations)
                .ChildRules(t =>
                {
                    t.RuleFor(x => x.LanguageCode).NotEmpty();
                    t.RuleFor(x => x.Name).NotEmpty();
                    t.RuleFor(x => x.Slug).NotEmpty();
                })
                .When(x => x.IsFullReplacement);

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
        }

        private static bool HaveUniqueLanguageCodes(
            IReadOnlyCollection<Lamie.Application.Settings.Products.Dtos.CreateProductTranslationDto> translations)
        {
            return translations
                .Select(translation => translation.LanguageCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == translations.Count;
        }
    }
}

