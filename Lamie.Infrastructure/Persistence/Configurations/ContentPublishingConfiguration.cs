using Lamie.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lamie.Infrastructure.Persistence.Configurations;

public sealed class ContentFooterSettingConfiguration
    : IEntityTypeConfiguration<ContentFooterSetting>
{
    public void Configure(EntityTypeBuilder<ContentFooterSetting> entity)
    {
        entity.ToTable("content_footer_settings");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Platform).HasConversion<int>();
        entity.Property(item => item.Content).HasMaxLength(2000);
        entity.Property(item => item.Hashtags).HasMaxLength(1000);
        entity.HasIndex(item => item.Platform).IsUnique();
        entity.HasIndex(item => new { item.IsActive, item.Platform });
    }
}

public sealed class ContentGenerationConfiguration
    : IEntityTypeConfiguration<ContentGeneration>
{
    public void Configure(EntityTypeBuilder<ContentGeneration> entity)
    {
        entity.ToTable("content_generations");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.SourceType).HasConversion<int>();
        entity.Property(item => item.Status)
            .HasConversion<int>()
            .IsConcurrencyToken();
        entity.Property(item => item.ProductNameSnapshot).HasMaxLength(300);
        entity.Property(item => item.ProductImageUrlSnapshot).HasMaxLength(2048);
        entity.Property(item => item.Brief).HasMaxLength(4000);
        entity.Property(item => item.StyleId).HasMaxLength(80);
        entity.Property(item => item.StyleName).HasMaxLength(160);
        entity.Property(item => item.Provider).HasMaxLength(80);
        entity.Property(item => item.Model).HasMaxLength(120);
        entity.Property(item => item.PromptVersion).HasMaxLength(80);
        entity.Property(item => item.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(120);
        entity.Property(item => item.RequestFingerprint).HasMaxLength(64);
        entity.HasIndex(item => new { item.Status, item.CreatedAt });
        entity.HasIndex(item => new { item.ProductId, item.CreatedAt });
        entity.HasIndex(item => item.ParentGenerationId);
        entity.HasIndex(item => new { item.ParentGenerationId, item.RequestFingerprint })
            .IsUnique()
            .HasFilter("[parent_generation_id] IS NOT NULL AND [request_fingerprint] IS NOT NULL");
        entity.HasIndex(item => new { item.CreatedBy, item.IdempotencyKey })
            .IsUnique()
            .HasFilter("[idempotency_key] IS NOT NULL");
        entity.HasMany(item => item.Items)
            .WithOne()
            .HasForeignKey(item => item.GenerationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasMany(item => item.Assets)
            .WithOne()
            .HasForeignKey(item => item.GenerationId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(item => item.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.Navigation(item => item.Assets).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ContentItemConfiguration : IEntityTypeConfiguration<ContentItem>
{
    public void Configure(EntityTypeBuilder<ContentItem> entity)
    {
        entity.ToTable("content_items");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Platform).HasConversion<int>();
        entity.Property(item => item.Body).HasMaxLength(10000);
        entity.Property(item => item.FooterSnapshot).HasMaxLength(2000);
        entity.Property(item => item.HashtagsSnapshot).HasMaxLength(1000);
        entity.Property(item => item.FullContent).HasMaxLength(14000);
        entity.HasIndex(item => new { item.GenerationId, item.Platform }).IsUnique();
        entity.HasIndex(item => item.Platform);
    }
}

public sealed class ContentAssetConfiguration : IEntityTypeConfiguration<ContentAsset>
{
    public void Configure(EntityTypeBuilder<ContentAsset> entity)
    {
        entity.ToTable("content_assets");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.PublicUrl).HasMaxLength(2048);
        entity.Property(item => item.FileName).HasMaxLength(260);
        entity.Property(item => item.ContentType).HasMaxLength(120);
        entity.HasIndex(item => new { item.GenerationId, item.SortOrder }).IsUnique();
    }
}
