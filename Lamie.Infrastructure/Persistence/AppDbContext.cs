using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lamie.Infrastructure.Persistence
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
        public DbSet<Expense> Expenses => Set<Expense>();
        public DbSet<Channel> Channels => Set<Channel>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<OrderImage> OrderImages => Set<OrderImage>();
        public DbSet<OrderChangeLog> OrderChangeLogs => Set<OrderChangeLog>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<AdminNavigation> Navigation => Set<AdminNavigation>();
        public DbSet<AccessAudit> AccessAudits => Set<AccessAudit>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();
        public DbSet<ProductImage> ProductImages => Set<ProductImage>();

        public DbSet<ProductCollection> ProductCollections => Set<ProductCollection>();
        public DbSet<ProductColor> ProductColors => Set<ProductColor>();
        public DbSet<ProductTag> ProductTags => Set<ProductTag>();
        public DbSet<ProductStyle> ProductStyles => Set<ProductStyle>();
        public DbSet<ProductOccasion> ProductOccasions => Set<ProductOccasion>();

        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<TagTranslation> TagTranslations => Set<TagTranslation>();

        public DbSet<Color> Colors => Set<Color>();
        public DbSet<ColorTranslation> ColorTranslations => Set<ColorTranslation>();

        public DbSet<Category> Categories => Set<Category>();
        public DbSet<CategoryTranslation> CategoryTranslations => Set<CategoryTranslation>();

        public DbSet<ProductType> ProductTypes => Set<ProductType>();
        public DbSet<ProductTypeTranslation> ProductTypeTranslations => Set<ProductTypeTranslation>();

        public DbSet<Collection> Collections => Set<Collection>();
        public DbSet<CollectionTranslation> CollectionTranslations => Set<CollectionTranslation>();

        public DbSet<Occasion> Occasions => Set<Occasion>();
        public DbSet<OccasionTranslation> OccasionTranslations => Set<OccasionTranslation>();

        public DbSet<Style> Styles => Set<Style>();
        public DbSet<StyleTranslation> StyleTranslations => Set<StyleTranslation>();

        public DbSet<Language> Languages => Set<Language>();

        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ReplaceService<
                IMigrationsSqlGenerator,
                ForeignKeyFreeSqlServerMigrationsSqlGenerator>();
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExpenseCategory>(entity =>
            {
                entity.ToTable("fin_expense_categories");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(120);
                entity.Property(x => x.NormalizedName).HasMaxLength(120);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.HasIndex(x => x.NormalizedName).IsUnique();
                entity.HasIndex(x => new { x.SortOrder, x.Name });
            });

            modelBuilder.Entity<Expense>(entity =>
            {
                entity.ToTable("fin_expenses");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ExpenseDate).HasColumnType("date");
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.Notes).HasMaxLength(2000);
                entity.HasIndex(x => x.ExpenseCategoryId);
                entity.HasIndex(x => new { x.ExpenseDate, x.ExpenseCategoryId });
            });

            modelBuilder.Entity<Channel>(entity =>
            {
                entity.ToTable("sales_channels");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Code).HasMaxLength(50);
                entity.Property(x => x.Name).HasMaxLength(200);
                entity.Property(x => x.IconUrl).HasMaxLength(2048);
                entity.Ignore(x => x.IsDefault);
                entity.HasIndex(x => x.Code).IsUnique();
                entity.HasIndex(x => new { x.SortOrder, x.Name });

                var seededAt = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
                entity.HasData(
                    new { Id = Channel.AdminId, Code = "admin", Name = "Admin", IconUrl = (string?)null, IsActive = true, SortOrder = 10, CreatedAt = seededAt, UpdatedAt = seededAt },
                    new { Id = Channel.WebsiteId, Code = "website", Name = "Website", IconUrl = (string?)null, IsActive = true, SortOrder = 20, CreatedAt = seededAt, UpdatedAt = seededAt },
                    new { Id = Channel.PhoneId, Code = "phone", Name = "Phone", IconUrl = (string?)null, IsActive = true, SortOrder = 30, CreatedAt = seededAt, UpdatedAt = seededAt },
                    new { Id = Channel.WalkInId, Code = "walk-in", Name = "Walk-in", IconUrl = (string?)null, IsActive = true, SortOrder = 40, CreatedAt = seededAt, UpdatedAt = seededAt },
                    new { Id = Channel.SocialId, Code = "social", Name = "Social", IconUrl = (string?)null, IsActive = true, SortOrder = 50, CreatedAt = seededAt, UpdatedAt = seededAt });
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("auth_users");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Email).HasMaxLength(320);
                entity.Property(x => x.NormalizedEmail).HasMaxLength(320);
                entity.Property(x => x.UserName).HasMaxLength(100);
                entity.Property(x => x.NormalizedUserName).HasMaxLength(100);
                entity.Property(x => x.PasswordHash).HasMaxLength(500);
                entity.Property(x => x.FullName).HasMaxLength(200);
                entity.Property(x => x.Phone).HasMaxLength(30);
                entity.Property(x => x.Role).HasConversion<int>();
                entity.Property(x => x.Status).HasConversion<int>();
                entity.HasIndex(x => x.NormalizedEmail).IsUnique();
                entity.HasIndex(x => x.NormalizedUserName).IsUnique();
            });

            var accessControlSeededAt = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
            modelBuilder.Entity<Role>(entity =>
            {
                entity.ToTable("auth_roles");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Code).HasMaxLength(80);
                entity.Property(x => x.Name).HasMaxLength(120);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.HasIndex(x => x.Code).IsUnique();
                entity.HasIndex(x => new { x.IsActive, x.Name });
                entity.HasData(
                    new
                    {
                        Id = Role.AdminId,
                        Code = "admin",
                        Name = "Quản trị viên",
                        Description = (string?)"Toàn quyền quản trị hệ thống.",
                        IsSystem = true,
                        IsActive = true,
                        CreatedAt = accessControlSeededAt,
                        UpdatedAt = accessControlSeededAt
                    },
                    new
                    {
                        Id = Role.ManagerId,
                        Code = "manager",
                        Name = "Quản lý",
                        Description = (string?)"Quản lý vận hành, cấu hình, chi phí và báo cáo.",
                        IsSystem = true,
                        IsActive = true,
                        CreatedAt = accessControlSeededAt,
                        UpdatedAt = accessControlSeededAt
                    },
                    new
                    {
                        Id = Role.StaffId,
                        Code = "staff",
                        Name = "Nhân viên",
                        Description = (string?)"Xử lý nghiệp vụ hàng ngày với quyền quản lý giới hạn.",
                        IsSystem = true,
                        IsActive = true,
                        CreatedAt = accessControlSeededAt,
                        UpdatedAt = accessControlSeededAt
                    });
            });

            modelBuilder.Entity<Permission>(entity =>
            {
                entity.ToTable("auth_permissions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Code).HasMaxLength(120);
                entity.Property(x => x.Name).HasMaxLength(160);
                entity.Property(x => x.Group).HasMaxLength(120);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.HasIndex(x => x.Code).IsUnique();
                entity.HasIndex(x => new { x.IsActive, x.Group, x.SortOrder, x.Name });
                entity.HasData(PermissionNames.Descriptors.Select((descriptor, index) => new
                {
                    Id = PermissionNames.IdFor(descriptor.Code),
                    descriptor.Code,
                    descriptor.Name,
                    descriptor.Group,
                    Description = (string?)descriptor.Description,
                    IsSystem = true,
                    IsActive = true,
                    SortOrder = (index + 1) * 10,
                    CreatedAt = accessControlSeededAt,
                    UpdatedAt = accessControlSeededAt
                }));
            });

            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.ToTable("auth_user_roles");
                entity.HasKey(x => new { x.UserId, x.RoleId });
                entity.HasIndex(x => x.UserId).IsUnique();
                entity.HasIndex(x => x.RoleId);
            });

            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.ToTable("auth_role_permissions");
                entity.HasKey(x => new { x.RoleId, x.PermissionId });
                entity.HasIndex(x => x.PermissionId);
                var grants = Enum.GetValues<BuiltInRole>()
                    .SelectMany(role => BuiltInRolePermissionDefaults.Get(role).Select(permissionCode => new
                    {
                        RoleId = Role.IdFor(role),
                        PermissionId = PermissionNames.IdFor(permissionCode),
                        GrantedAt = accessControlSeededAt
                    }));
                entity.HasData(grants);
            });

            modelBuilder.Entity<AdminNavigation>(entity =>
            {
                entity.ToTable("auth_navigation");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Key).HasMaxLength(120);
                entity.Property(x => x.ModuleKey).HasMaxLength(120);
                entity.Property(x => x.PageKey).HasMaxLength(160);
                entity.Property(x => x.Label).HasMaxLength(160);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.Path).HasMaxLength(400);
                entity.Property(x => x.IconKey).HasMaxLength(80);
                entity.Property(x => x.PermissionCode).HasMaxLength(120);
                entity.HasIndex(x => x.Key).IsUnique();
                entity.HasIndex(x => new { x.ParentId, x.SortOrder, x.Label });
                entity.HasIndex(x => x.PermissionCode);
                entity.HasIndex(x => new { x.ModuleKey, x.PageKey });
                entity.HasIndex(x => new { x.IsEnabled, x.IsVisible });
            });

            modelBuilder.Entity<AccessAudit>(entity =>
            {
                entity.ToTable("auth_access_audit");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Action).HasMaxLength(80);
                entity.Property(x => x.EntityType).HasMaxLength(80);
                entity.Property(x => x.EntityId).HasMaxLength(160);
                entity.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)");
                entity.Property(x => x.AfterJson).HasColumnType("nvarchar(max)");
                entity.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt });
                entity.HasIndex(x => new { x.ActorUserId, x.OccurredAt });
            });

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.ToTable("auth_refresh_tokens");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.TokenHash).HasMaxLength(64);
                entity.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
                entity.Property(x => x.CreatedByIp).HasMaxLength(64);
                entity.Property(x => x.RevokedByIp).HasMaxLength(64);
                entity.HasIndex(x => x.TokenHash).IsUnique();
                entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Customer>(entity =>
            {
                entity.ToTable("crm_customers");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).HasMaxLength(200);
                entity.Property(x => x.Phone).HasMaxLength(30);
                entity.Property(x => x.NormalizedPhone).HasMaxLength(20);
                entity.Property(x => x.Email).HasMaxLength(320);
                entity.Property(x => x.NormalizedEmail).HasMaxLength(320);
                entity.Property(x => x.Notes).HasMaxLength(4000);
                entity.HasIndex(x => x.NormalizedPhone);
                entity.HasIndex(x => x.NormalizedEmail)
                    .HasFilter("[normalized_email] IS NOT NULL");
                entity.HasIndex(x => x.CreatedAt);
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("sales_orders");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.OrderCode).HasMaxLength(40);
                entity.Property(x => x.OrdererName).HasMaxLength(200);
                entity.Property(x => x.OrdererPhone).HasMaxLength(30);
                entity.Property(x => x.RecipientName).HasMaxLength(200);
                entity.Property(x => x.RecipientPhone).HasMaxLength(30);
                entity.Property(x => x.ProvinceShipping);
                entity.Property(x => x.DeliveryAddress).HasMaxLength(1000);
                entity.Property(x => x.DeliveryAddressDescription).HasMaxLength(1000);
                entity.Property(x => x.DeliveryLatitude).HasPrecision(9, 6);
                entity.Property(x => x.DeliveryLongitude).HasPrecision(9, 6);
                entity.Property(x => x.DeliveryTo);
                entity.Property(x => x.DepositAmount).HasPrecision(18, 2);
                entity.Property(x => x.ShippingFee).HasPrecision(18, 2);
                entity.Property(x => x.ShippingFeeActual).HasPrecision(18, 2);
                entity.Property(x => x.SubTotal).HasPrecision(18, 2);
                entity.Property(x => x.DiscountTotal).HasPrecision(18, 2);
                entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
                entity.Property(x => x.PaymentStatus).HasConversion<int>();
                entity.Property(x => x.OrderStatus).HasConversion<int>();
                entity.Property(x => x.Description).HasMaxLength(4000);
                entity.Property(x => x.ContentNote).HasMaxLength(4000);
                entity.Property(x => x.RowVersion).IsRowVersion();
                entity.HasIndex(x => x.OrderCode).IsUnique();
                entity.HasIndex(x => x.CreatedAt);
                entity.HasIndex(x => new { x.OrderStatus, x.CreatedAt });
                entity.HasIndex(x => new { x.PaymentStatus, x.CreatedAt });
                entity.HasIndex(x => new { x.ChannelId, x.CreatedAt });
                entity.HasIndex(x => new { x.DeliveryAt, x.OrderStatus });
                entity.HasIndex(x => new { x.CustomerId, x.CreatedAt });

                entity.HasOne<Channel>()
                    .WithMany()
                    .HasForeignKey(x => x.ChannelId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Customer>()
                    .WithMany()
                    .HasForeignKey(x => x.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.CreatedById)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.UpdatedById)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Metadata.FindNavigation(nameof(Order.Items))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.Metadata.FindNavigation(nameof(Order.Images))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.Metadata.FindNavigation(nameof(Order.ChangeLogs))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.HasMany(x => x.Items)
                    .WithOne()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(x => x.Images)
                    .WithOne()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(x => x.ChangeLogs)
                    .WithOne()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("sales_order_items");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ProductSku).HasMaxLength(100);
                entity.Property(x => x.ProductName).HasMaxLength(300);
                entity.Property(x => x.ThumbnailUrl).HasMaxLength(2048);
                entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
                entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
                entity.Property(x => x.LineTotal).HasPrecision(18, 2);
                entity.Property(x => x.Note).HasMaxLength(1000);
                entity.HasIndex(x => x.OrderId);
                entity.HasIndex(x => x.ProductId);
                entity.HasOne<Product>()
                    .WithMany()
                    .HasForeignKey(x => x.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<OrderImage>(entity =>
            {
                entity.ToTable("sales_order_images");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ImageUrl).HasMaxLength(2048);
                entity.Property(x => x.Description).HasMaxLength(1000);
                entity.HasIndex(x => new { x.OrderId, x.SortOrder });
                entity.HasIndex(x => x.OrderItemId);
            });

            modelBuilder.Entity<OrderChangeLog>(entity =>
            {
                entity.ToTable("sales_order_change_logs");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.EntityName).HasMaxLength(100);
                entity.Property(x => x.FieldName).HasMaxLength(100);
                entity.Property(x => x.OldValue).HasMaxLength(2000);
                entity.Property(x => x.NewValue).HasMaxLength(2000);
                entity.Property(x => x.ChangeType).HasMaxLength(100);
                entity.Property(x => x.ChangedByName).HasMaxLength(200);
                entity.Property(x => x.Note).HasMaxLength(2000);
                entity.HasIndex(x => new { x.OrderId, x.ChangedAt });
                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.ChangedById)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Product
            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("cat_products");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Sku)
                      .HasColumnName("sku")
                      .HasMaxLength(100);
                entity.Property(x => x.Price)
                      .HasColumnName("price")
                      .HasPrecision(18, 2);
                entity.Property(x => x.SalePrice)
                      .HasColumnName("sale_price")
                      .HasPrecision(18, 2);
                entity.Property(x => x.Stock).HasColumnName("stock");
                entity.Property(x => x.TracksInventory)
                      .HasColumnName("tracks_inventory");
                entity.Property(x => x.CategoryId).HasColumnName("category_id");
                entity.Property(x => x.ProductTypeId).HasColumnName("product_type_id");
                entity.Property(x => x.IsActive).HasColumnName("is_active");
                entity.HasIndex(x => new { x.IsActive, x.Stock });
                entity.Property(x => x.RowVersion).IsRowVersion();

                entity.HasIndex(x => x.Sku).IsUnique();

                entity.HasOne<Category>()
                      .WithMany()
                      .HasForeignKey(x => x.CategoryId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<ProductType>()
                      .WithMany()
                      .HasForeignKey(x => x.ProductTypeId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.Metadata.FindNavigation(nameof(Product.Translations))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.Metadata.FindNavigation(nameof(Product.Images))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.Metadata.FindNavigation(nameof(Product.Collections))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.Metadata.FindNavigation(nameof(Product.Colors))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.Metadata.FindNavigation(nameof(Product.Tags))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.Metadata.FindNavigation(nameof(Product.Styles))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.Metadata.FindNavigation(nameof(Product.Occasions))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);

                entity.HasMany(p => p.Translations)
                      .WithOne()
                      .HasForeignKey(t => t.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(p => p.Images)
                      .WithOne()
                      .HasForeignKey(i => i.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(p => p.Collections)
                      .WithOne()
                      .HasForeignKey(r => r.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(p => p.Colors)
                      .WithOne()
                      .HasForeignKey(r => r.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(p => p.Tags)
                      .WithOne()
                      .HasForeignKey(r => r.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(p => p.Styles)
                      .WithOne()
                      .HasForeignKey(r => r.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(p => p.Occasions)
                      .WithOne()
                      .HasForeignKey(r => r.ProductId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ProductTranslation>(entity =>
            {
                entity.ToTable("cat_product_translations");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ProductId).HasColumnName("product_id");
                entity.Property(x => x.LanguageCode).HasColumnName("language_code");
                entity.Property(x => x.Name).HasColumnName("name");
                entity.Property(x => x.Slug).HasColumnName("slug");
                entity.Property(x => x.Description).HasColumnName("description");

                entity.Property(x => x.LanguageCode).HasMaxLength(10);
                entity.HasIndex(x => new { x.ProductId, x.LanguageCode }).IsUnique();
                entity.HasOne<Language>()
                      .WithMany()
                      .HasForeignKey(x => x.LanguageCode)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductImage>(entity =>
            {
                entity.ToTable("cat_product_images");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ProductId).HasColumnName("product_id");
                entity.Property(x => x.ImageUrl).HasColumnName("image_url");
                entity.Property(x => x.IsActive).HasColumnName("is_active");
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
            });

            // Product relationships
            modelBuilder.Entity<ProductCollection>(entity =>
            {
                entity.ToTable("rel_product_collections");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ProductId).HasColumnName("product_id");
                entity.Property(x => x.CollectionId).HasColumnName("collection_id");
                entity.HasIndex(x => new { x.ProductId, x.CollectionId }).IsUnique();
                entity.HasOne<Collection>()
                      .WithMany()
                      .HasForeignKey(x => x.CollectionId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductColor>(entity =>
            {
                entity.ToTable("rel_product_colors");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ProductId).HasColumnName("product_id");
                entity.Property(x => x.ColorId).HasColumnName("color_id");
                entity.HasIndex(x => new { x.ProductId, x.ColorId }).IsUnique();
                entity.HasOne<Color>()
                      .WithMany()
                      .HasForeignKey(x => x.ColorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductTag>(entity =>
            {
                entity.ToTable("rel_product_tags");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ProductId).HasColumnName("product_id");
                entity.Property(x => x.TagId).HasColumnName("tag_id");
                entity.HasIndex(x => new { x.ProductId, x.TagId }).IsUnique();
                entity.HasOne<Tag>()
                      .WithMany()
                      .HasForeignKey(x => x.TagId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductStyle>(entity =>
            {
                entity.ToTable("rel_product_styles");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ProductId).HasColumnName("product_id");
                entity.Property(x => x.StyleId).HasColumnName("style_id");
                entity.HasIndex(x => new { x.ProductId, x.StyleId }).IsUnique();
                entity.HasOne<Style>()
                      .WithMany()
                      .HasForeignKey(x => x.StyleId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ProductOccasion>(entity =>
            {
                entity.ToTable("rel_product_occasions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ProductId).HasColumnName("product_id");
                entity.Property(x => x.OccasionId).HasColumnName("occasion_id");
                entity.HasIndex(x => new { x.ProductId, x.OccasionId }).IsUnique();
                entity.HasOne<Occasion>()
                      .WithMany()
                      .HasForeignKey(x => x.OccasionId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Tags
            modelBuilder.Entity<Tag>(entity =>
            {
                entity.ToTable("md_tags");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.IsActive)
                      .HasColumnName("is_active");

                // Map backing field _translations
                entity.Metadata.FindNavigation(nameof(Tag.Translations))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);

                entity.HasMany(t => t.Translations)
                      .WithOne()
                      .HasForeignKey(t => t.TagId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TagTranslation>(entity =>
            {
                entity.ToTable("md_tag_translations");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.TagId)
                      .HasColumnName("tag_id");

                entity.Property(x => x.LanguageCode)
                      .HasColumnName("language_code");

                entity.Property(x => x.Name)
                      .HasColumnName("name");

                entity.Property(x => x.Description)
                      .HasColumnName("description");

                entity.Property(x => x.LanguageCode).HasMaxLength(10);
                entity.HasIndex(x => new { x.TagId, x.LanguageCode }).IsUnique();
                entity.HasOne<Language>()
                      .WithMany()
                      .HasForeignKey(x => x.LanguageCode)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Colors
            modelBuilder.Entity<Color>(entity =>
            {
                entity.ToTable("md_colors");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.HexCode)
                      .HasColumnName("hex_code");

                entity.Property(x => x.RgbCode)
                      .HasColumnName("rgb_code");

                entity.Property(x => x.IsActive)
                      .HasColumnName("is_active");

                entity.Metadata.FindNavigation(nameof(Color.Translations))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);

                entity.HasMany(c => c.Translations)
                      .WithOne()
                      .HasForeignKey(t => t.ColorId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ColorTranslation>(entity =>
            {
                entity.ToTable("md_color_translations");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ColorId)
                      .HasColumnName("color_id");

                entity.Property(x => x.LanguageCode)
                      .HasColumnName("language_code");

                entity.Property(x => x.Name)
                      .HasColumnName("name");

                entity.Property(x => x.Description)
                      .HasColumnName("description");

                entity.Property(x => x.LanguageCode).HasMaxLength(10);
                entity.HasIndex(x => new { x.ColorId, x.LanguageCode }).IsUnique();
                entity.HasOne<Language>()
                      .WithMany()
                      .HasForeignKey(x => x.LanguageCode)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Categories
            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("md_categories");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.SortOrder)
                      .HasColumnName("sort_order");

                entity.Property(x => x.IsActive)
                      .HasColumnName("is_active");

                entity.Metadata.FindNavigation(nameof(Category.Translations))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);

                entity.HasMany(c => c.Translations)
                      .WithOne()
                      .HasForeignKey(t => t.CategoryId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CategoryTranslation>(entity =>
            {
                entity.ToTable("md_category_translations");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.CategoryId)
                      .HasColumnName("category_id");

                entity.Property(x => x.LanguageCode)
                      .HasColumnName("language_code");

                entity.Property(x => x.Name)
                      .HasColumnName("name");

                entity.Property(x => x.Description)
                      .HasColumnName("description");

                entity.Property(x => x.LanguageCode).HasMaxLength(10);
                entity.HasIndex(x => new { x.CategoryId, x.LanguageCode }).IsUnique();
                entity.HasOne<Language>()
                      .WithMany()
                      .HasForeignKey(x => x.LanguageCode)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Product types
            modelBuilder.Entity<ProductType>(entity =>
            {
                entity.ToTable("md_product_types");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
                entity.Property(x => x.SortOrder).HasColumnName("sort_order");
                entity.Property(x => x.IsActive).HasColumnName("is_active");
                entity.HasIndex(x => x.Code).IsUnique();
                entity.Metadata.FindNavigation(nameof(ProductType.Translations))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);
                entity.HasMany(x => x.Translations)
                    .WithOne()
                    .HasForeignKey(x => x.ProductTypeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ProductTypeTranslation>(entity =>
            {
                entity.ToTable("md_product_type_translations");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.ProductTypeId).HasColumnName("product_type_id");
                entity.Property(x => x.LanguageCode).HasColumnName("language_code").HasMaxLength(10);
                entity.Property(x => x.Name).HasColumnName("name");
                entity.Property(x => x.Description).HasColumnName("description");
                entity.HasIndex(x => new { x.ProductTypeId, x.LanguageCode }).IsUnique();
                entity.HasOne<Language>()
                    .WithMany()
                    .HasForeignKey(x => x.LanguageCode)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Collections
            modelBuilder.Entity<Collection>(entity =>
            {
                entity.ToTable("md_collections");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.IsActive)
                      .HasColumnName("is_active");

                entity.Metadata.FindNavigation(nameof(Collection.Translations))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);

                entity.HasMany(c => c.Translations)
                      .WithOne()
                      .HasForeignKey(t => t.CollectionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CollectionTranslation>(entity =>
            {
                entity.ToTable("md_collection_translations");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.CollectionId)
                      .HasColumnName("collection_id");

                entity.Property(x => x.LanguageCode)
                      .HasColumnName("language_code");

                entity.Property(x => x.Name)
                      .HasColumnName("name");

                entity.Property(x => x.Description)
                      .HasColumnName("description");

                entity.Property(x => x.LanguageCode).HasMaxLength(10);
                entity.HasIndex(x => new { x.CollectionId, x.LanguageCode }).IsUnique();
                entity.HasOne<Language>()
                      .WithMany()
                      .HasForeignKey(x => x.LanguageCode)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Occasions
            modelBuilder.Entity<Occasion>(entity =>
            {
                entity.ToTable("md_occasions");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.IsActive)
                      .HasColumnName("is_active");

                entity.Metadata.FindNavigation(nameof(Occasion.Translations))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);

                entity.HasMany(o => o.Translations)
                      .WithOne()
                      .HasForeignKey(t => t.OccasionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OccasionTranslation>(entity =>
            {
                entity.ToTable("md_occasion_translations");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.OccasionId)
                      .HasColumnName("occasion_id");

                entity.Property(x => x.LanguageCode)
                      .HasColumnName("language_code");

                entity.Property(x => x.Name)
                      .HasColumnName("name");

                entity.Property(x => x.Description)
                      .HasColumnName("description");

                entity.Property(x => x.LanguageCode).HasMaxLength(10);
                entity.HasIndex(x => new { x.OccasionId, x.LanguageCode }).IsUnique();
                entity.HasOne<Language>()
                      .WithMany()
                      .HasForeignKey(x => x.LanguageCode)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Styles
            modelBuilder.Entity<Style>(entity =>
            {
                entity.ToTable("md_styles");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.IsActive)
                      .HasColumnName("is_active");

                entity.Metadata.FindNavigation(nameof(Style.Translations))!
                    .SetPropertyAccessMode(PropertyAccessMode.Field);

                entity.HasMany(s => s.Translations)
                      .WithOne()
                      .HasForeignKey(t => t.StyleId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StyleTranslation>(entity =>
            {
                entity.ToTable("md_style_translations");
                entity.HasKey(x => x.Id);

                entity.Property(x => x.StyleId)
                      .HasColumnName("style_id");

                entity.Property(x => x.LanguageCode)
                      .HasColumnName("language_code");

                entity.Property(x => x.Name)
                      .HasColumnName("name");

                entity.Property(x => x.Description)
                      .HasColumnName("description");

                entity.Property(x => x.LanguageCode).HasMaxLength(10);
                entity.HasIndex(x => new { x.StyleId, x.LanguageCode }).IsUnique();
                entity.HasOne<Language>()
                      .WithMany()
                      .HasForeignKey(x => x.LanguageCode)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Language>(entity =>
            {
                entity.ToTable("sys_languages");
                entity.HasKey(x => x.Code);

                entity.Property(x => x.Code)
                      .HasColumnName("code")
                      .HasMaxLength(10);

                entity.Property(x => x.Name)
                      .HasColumnName("name");

                entity.Property(x => x.IsActive)
                      .HasColumnName("is_active");
            });
        }
        public override int SaveChanges()
        {
            ApplyAudit();
            return base.SaveChanges();
        }
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAudit();
            return await base.SaveChangesAsync(cancellationToken);
        }
        private void ApplyAudit()
        {
            var now = DateTime.UtcNow;

            foreach (var entry in ChangeTracker.Entries<Entity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAt = now;
                        entry.Entity.CreatedBy = -99;
                        entry.Entity.CreatedName = "dev";
                        entry.Entity.UpdatedAt = now;
                        entry.Entity.UpdatedBy = -99;
                        entry.Entity.UpdatedName = "dev";
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedAt = now;
                        entry.Entity.UpdatedBy = -100;
                        entry.Entity.UpdatedName = "test";
                        break;
                }
            }
        }
    }
}
