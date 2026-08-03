using Lamie.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lamie.Infrastructure.Persistence.Configurations
{
    public class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.ToTable("cat_products");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.Sku)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(p => p.Price)
                .HasColumnType("numeric(18,2)");

            builder.Property(p => p.SalePrice)
                .HasColumnType("numeric(18,2)");

            builder.Property(p => p.Stock);

            builder.Property(p => p.IsActive);

            builder.Property(p => p.ThumbnailUrl)
                .HasColumnName("thumbnail_url")
                .HasMaxLength(500)
                .IsRequired(false);

            builder.Property(p => p.CreatedAt);
            builder.Property(p => p.UpdatedAt);

            //// Aggregate relationship
            //builder.HasMany(p => p.Translations)
            //    .WithOne()
            //    .HasForeignKey("ProductId")
            //    .OnDelete(DeleteBehavior.Cascade);

            //builder.HasMany(p => p.Images)
            //    .WithOne()
            //    .HasForeignKey("ProductId")
            //    .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
