using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FlattenInventoryNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();

                UPDATE auth_navigation
                SET parent_id = NULL, sort_order = 900, is_visible = 0, updated_at = @now
                WHERE [key] = N'inventory.group' AND is_system = 1;

                UPDATE node
                SET parent_id = parent.id,
                    sort_order = mapping.sort_order,
                    label = mapping.label,
                    is_visible = mapping.is_visible,
                    updated_at = @now
                FROM auth_navigation node
                INNER JOIN (VALUES
                    (N'ingredients.inventory', N'group.operations', 20, N'Kho hàng', CAST(1 AS bit)),
                    (N'ingredients.inventory-receiving', N'ingredients.inventory', 20, N'Nhập kho', CAST(0 AS bit)),
                    (N'ingredients.inventory-transactions', N'ingredients.inventory', 30, N'Lịch sử giao dịch', CAST(0 AS bit))
                ) mapping([key], parent_key, sort_order, label, is_visible) ON mapping.[key] = node.[key]
                INNER JOIN auth_navigation parent ON parent.[key] = mapping.parent_key
                WHERE node.is_system = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();

                UPDATE node
                SET parent_id = parent.id,
                    sort_order = mapping.sort_order,
                    label = mapping.label,
                    is_visible = mapping.is_visible,
                    updated_at = @now
                FROM auth_navigation node
                INNER JOIN (VALUES
                    (N'ingredients.inventory', N'inventory.group', 10, N'Tồn kho hiện tại', CAST(1 AS bit)),
                    (N'ingredients.inventory-receiving', N'inventory.group', 20, N'Nhập kho', CAST(1 AS bit)),
                    (N'ingredients.inventory-transactions', N'inventory.group', 30, N'Lịch sử giao dịch', CAST(1 AS bit))
                ) mapping([key], parent_key, sort_order, label, is_visible) ON mapping.[key] = node.[key]
                INNER JOIN auth_navigation parent ON parent.[key] = mapping.parent_key
                WHERE node.is_system = 1;

                UPDATE node
                SET parent_id = parent.id, sort_order = 20, is_visible = 1, updated_at = @now
                FROM auth_navigation node
                INNER JOIN auth_navigation parent ON parent.[key] = N'group.operations'
                WHERE node.[key] = N'inventory.group' AND node.is_system = 1;
                """);
        }
    }
}
