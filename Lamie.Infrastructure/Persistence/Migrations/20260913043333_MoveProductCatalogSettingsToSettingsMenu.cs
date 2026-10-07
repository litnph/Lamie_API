using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveProductCatalogSettingsToSettingsMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @parent uniqueidentifier = (
                    SELECT TOP (1) id
                    FROM auth_navigation
                    WHERE [key] = N'catalog-settings.home');
                DECLARE @now datetime2 = SYSUTCDATETIME();

                IF @parent IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM auth_navigation WHERE [key] = N'products.price-settings')
                BEGIN
                    INSERT INTO auth_navigation
                        (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                         permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                         created_at, updated_at, created_by, updated_by)
                    VALUES
                        ('90000000-0000-4000-8000-000000000052', N'products.price-settings', @parent,
                         N'products', N'products.price-settings', N'Gợi ý theo giá', NULL,
                         N'/admin/settings/catalog/product-pricing', N'settings', N'products.manage',
                         10, 0, 1, 1, 0, @now, @now, NULL, NULL);
                END;

                IF @parent IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM auth_navigation WHERE [key] = N'products.branding-settings')
                BEGIN
                    INSERT INTO auth_navigation
                        (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                         permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                         created_at, updated_at, created_by, updated_by)
                    VALUES
                        ('90000000-0000-4000-8000-000000000053', N'products.branding-settings', @parent,
                         N'products', N'products.branding-settings', N'Logo & watermark', NULL,
                         N'/admin/settings/catalog/branding', N'shield-check', N'products.manage',
                         20, 0, 1, 1, 0, @now, @now, NULL, NULL);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM auth_navigation
                WHERE [key] IN (N'products.price-settings', N'products.branding-settings')
                  AND is_system = 1;
                """);
        }
    }
}
