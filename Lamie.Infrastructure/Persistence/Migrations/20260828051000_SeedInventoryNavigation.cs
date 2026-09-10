using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260828051000_SeedInventoryNavigation")]
    public partial class SeedInventoryNavigation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();
                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT CAST('90000000-0000-4000-8000-000000000032' AS uniqueidentifier),
                       N'ingredients.inventory', parent.id, N'ingredients', N'ingredients.inventory',
                       N'Kho nguyên liệu', NULL, N'/admin/ingredients/inventory', N'package-open',
                       N'ingredients.view', 20, 1, 1, 1, 0, @now, @now, NULL, NULL
                FROM auth_navigation parent
                WHERE parent.[key] = N'ingredients.list'
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.[key] = N'ingredients.inventory')
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.id = '90000000-0000-4000-8000-000000000032');

                UPDATE auth_navigation SET sort_order = 30, updated_at = @now
                WHERE [key] = N'ingredients.demand' AND sort_order < 30;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM auth_navigation
                WHERE id = '90000000-0000-4000-8000-000000000032'
                  AND [key] = N'ingredients.inventory';
                """);
        }
    }
}
