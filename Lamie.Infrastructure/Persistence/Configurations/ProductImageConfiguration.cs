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
    public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
    {
        public void Configure(EntityTypeBuilder<ProductImage> builder)
        {
            builder.ToTable("cat_product_images");

            builder.HasKey("Id");

            builder.Property<int>("Id");

            builder.Property<string>("ImageUrl")
                .HasColumnType("text")
                .IsRequired();

            builder.Property<int>("SortOrder");

            builder.Property<bool>("IsActive")
                .HasDefaultValue(true);
        }
    }
}
