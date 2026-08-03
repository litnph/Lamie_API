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
    public class ProductTranslationConfiguration : IEntityTypeConfiguration<ProductTranslation>
    {
        public void Configure(EntityTypeBuilder<ProductTranslation> builder)
        {
            builder.ToTable("cat_product_translations");

            builder.HasKey("Id");

            builder.Property<int>("Id");

            builder.Property<string>("LanguageCode")
                .HasMaxLength(10)
                .IsRequired();

            builder.Property<string>("Name")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property<string>("Description")
                .HasColumnType("text");
        }
    }
}
