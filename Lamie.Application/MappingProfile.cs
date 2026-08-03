using AutoMapper;
using Lamie.Application.MasterData.Tags;
using Lamie.Application.Settings.Attributes.Languages;
using Lamie.Application.Settings.Attributes.Colors;
using Lamie.Application.Settings.Attributes.Categories;
using Lamie.Application.Settings.Attributes.Collections;
using Lamie.Application.Settings.Attributes.Occasions;
using Lamie.Application.Settings.Attributes.Styles;
using Lamie.Application.Settings.Attributes.ProductTypes;
using Lamie.Domain.Entities;

namespace Lamie.Application;

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Languages
        CreateMap<Language, LanguageDto>();

        // Tags
        CreateMap<TagTranslation, TagTranslationDto>();
        CreateMap<Tag, TagDto>();

        // Colors
        CreateMap<ColorTranslation, ColorTranslationDto>();
        CreateMap<Color, ColorDto>();

        // Categories
        CreateMap<CategoryTranslation, CategoryTranslationDto>();
        CreateMap<Category, CategoryDto>();

        // Product types
        CreateMap<ProductTypeTranslation, ProductTypeTranslationDto>();
        CreateMap<ProductType, ProductTypeDto>();

        // Collections
        CreateMap<CollectionTranslation, CollectionTranslationDto>();
        CreateMap<Collection, CollectionDto>();

        // Occasions
        CreateMap<OccasionTranslation, OccasionTranslationDto>();
        CreateMap<Occasion, OccasionDto>();

        // Styles
        CreateMap<StyleTranslation, StyleTranslationDto>();
        CreateMap<Style, StyleDto>();
    }
}

