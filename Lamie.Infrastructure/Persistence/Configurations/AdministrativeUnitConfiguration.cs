using Lamie.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lamie.Infrastructure.Persistence.Configurations;

public sealed class AdministrativeUnitConfiguration : IEntityTypeConfiguration<AdministrativeUnit>
{
    public void Configure(EntityTypeBuilder<AdministrativeUnit> entity)
    {
        entity.ToTable("geo_administrative_units");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(10);
        entity.Property(x => x.Name).HasMaxLength(160);
        entity.Property(x => x.FullName).HasMaxLength(200);
        entity.Property(x => x.NormalizedName).HasMaxLength(200);
        entity.Property(x => x.Scheme).HasConversion<int>();
        entity.Property(x => x.UnitType).HasConversion<int>();
        entity.Property(x => x.ParentCode).HasMaxLength(10);
        entity.Property(x => x.EffectiveFrom).HasColumnType("date");
        entity.Property(x => x.EffectiveTo).HasColumnType("date");
        entity.Property(x => x.SourceDocument).HasMaxLength(160);
        entity.Property(x => x.SourceReference).HasMaxLength(1000);
        entity.Property(x => x.DatasetVersion).HasMaxLength(80);
        entity.HasIndex(x => new { x.Scheme, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.Scheme, x.ParentCode, x.SortOrder });
        entity.HasIndex(x => new { x.Scheme, x.NormalizedName });
        entity.HasIndex(x => new { x.Scheme, x.UnitType, x.IsActive });
    }
}

public sealed class AdministrativeUnitTransitionConfiguration : IEntityTypeConfiguration<AdministrativeUnitTransition>
{
    public void Configure(EntityTypeBuilder<AdministrativeUnitTransition> entity)
    {
        entity.ToTable("geo_administrative_unit_transitions");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.LegacyUnitCode).HasMaxLength(10);
        entity.Property(x => x.CurrentUnitCode).HasMaxLength(10);
        entity.Property(x => x.TransitionType).HasConversion<int>();
        entity.Property(x => x.SourceDocument).HasMaxLength(160);
        entity.Property(x => x.SourceReference).HasMaxLength(1000);
        entity.Property(x => x.EffectiveDate).HasColumnType("date");
        entity.Property(x => x.DatasetVersion).HasMaxLength(80);
        entity.Property(x => x.Note).HasMaxLength(1000);
        entity.HasIndex(x => new { x.LegacyUnitCode, x.CurrentUnitCode }).IsUnique();
        entity.HasIndex(x => x.LegacyUnitCode);
        entity.HasIndex(x => x.CurrentUnitCode);
        entity.HasIndex(x => x.TransitionType);
    }
}
