using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateDailyPreparationAndInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inv_stock_units_ingredient_id_size_key",
                table: "inv_stock_units");

            migrationBuilder.AlterColumn<int>(
                name: "ingredient_id",
                table: "sales_order_materials",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "inventory_item_id",
                table: "sales_order_materials",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<int>(
                name: "ingredient_id",
                table: "inv_stock_units",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "inventory_item_id",
                table: "inv_stock_units",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "inv_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_ingredient_id = table.Column<int>(type: "int", nullable: true),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    measurement_unit_id = table.Column<int>(type: "int", nullable: false),
                    note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_items", x => x.id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO inv_items
                    (id, legacy_ingredient_id, code, name, measurement_unit_id, note, is_active, created_at, updated_at)
                SELECT NEWID(), ingredient.id, ingredient.code, ingredient.name, ingredient.base_unit_id,
                       ingredient.note, ingredient.is_active, SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM md_ingredients ingredient
                WHERE EXISTS (
                    SELECT 1 FROM inv_stock_units stock_unit
                    WHERE stock_unit.ingredient_id = ingredient.id)
                  AND NOT EXISTS (
                    SELECT 1 FROM inv_items existing
                    WHERE existing.legacy_ingredient_id = ingredient.id);

                UPDATE stock_unit
                SET inventory_item_id = inventory_item.id
                FROM inv_stock_units stock_unit
                INNER JOIN inv_items inventory_item
                    ON inventory_item.legacy_ingredient_id = stock_unit.ingredient_id;

                UPDATE order_material
                SET inventory_item_id = stock_unit.inventory_item_id
                FROM sales_order_materials order_material
                INNER JOIN inv_stock_units stock_unit
                    ON stock_unit.id = order_material.stock_unit_id;

                IF EXISTS (SELECT 1 FROM inv_stock_units WHERE inventory_item_id = '00000000-0000-0000-0000-000000000000')
                    THROW 51000, 'Cannot migrate an inventory stock unit whose legacy ingredient no longer exists.', 1;
                IF EXISTS (SELECT 1 FROM sales_order_materials WHERE inventory_item_id = '00000000-0000-0000-0000-000000000000')
                    THROW 51000, 'Cannot migrate an order inventory usage whose stock unit no longer exists.', 1;

                DECLARE @inventory_parent uniqueidentifier = (
                    SELECT TOP 1 id FROM auth_navigation WHERE [key] = N'group.management');
                UPDATE auth_navigation
                SET parent_id = @inventory_parent,
                    label = N'Kho tồn',
                    permission_code = N'inventory.view',
                    sort_order = 45,
                    updated_at = SYSUTCDATETIME()
                WHERE [key] = N'ingredients.inventory';

                UPDATE auth_navigation
                SET label = N'Nguyên liệu cần chuẩn bị', updated_at = SYSUTCDATETIME()
                WHERE [key] = N'ingredients.demand';
                """);

            migrationBuilder.InsertData(
                table: "auth_permissions",
                columns: new[] { "id", "code", "created_at", "description", "group", "is_active", "is_system", "name", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000027"), "inventory.view", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Xem hàng tồn, số lượng, cảnh báo và lịch sử giao dịch.", "Kho tồn", true, true, "Xem kho tồn", 270, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000028"), "inventory.manage", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Tạo vật tư kho, cấu hình size, nhập kho và điều chỉnh usage của đơn hàng.", "Kho tồn", true, true, "Quản lý kho tồn", 280, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "auth_role_permissions",
                columns: new[] { "permission_id", "role_id", "granted_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000027"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000028"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000027"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000028"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000027"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_units_inventory_item_id_size_key",
                table: "inv_stock_units",
                columns: new[] { "inventory_item_id", "size_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_items_code",
                table: "inv_items",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_items_is_active_name",
                table: "inv_items",
                columns: new[] { "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_items_legacy_ingredient_id",
                table: "inv_items",
                column: "legacy_ingredient_id",
                unique: true,
                filter: "[legacy_ingredient_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inv_items_measurement_unit_id",
                table: "inv_items",
                column: "measurement_unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM inv_stock_units WHERE ingredient_id IS NULL)
                    THROW 51000, 'Cannot roll back inventory separation after independent inventory items have been created.', 1;

                UPDATE auth_navigation
                SET parent_id = (SELECT TOP 1 id FROM auth_navigation WHERE [key] = N'ingredients.list'),
                    label = N'Kho nguyên liệu',
                    permission_code = N'ingredients.view',
                    sort_order = 20,
                    updated_at = SYSUTCDATETIME()
                WHERE [key] = N'ingredients.inventory';

                UPDATE auth_navigation
                SET label = N'Nhu cầu nguyên liệu', updated_at = SYSUTCDATETIME()
                WHERE [key] = N'ingredients.demand';
                """);

            migrationBuilder.DropTable(
                name: "inv_items");

            migrationBuilder.DropIndex(
                name: "ix_inv_stock_units_inventory_item_id_size_key",
                table: "inv_stock_units");

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000027"));

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000028"));

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000027"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000028"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000027"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000028"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000027"), new Guid("20000000-0000-4000-8000-000000000003") });

            migrationBuilder.DropColumn(
                name: "inventory_item_id",
                table: "sales_order_materials");

            migrationBuilder.DropColumn(
                name: "inventory_item_id",
                table: "inv_stock_units");

            migrationBuilder.AlterColumn<int>(
                name: "ingredient_id",
                table: "sales_order_materials",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ingredient_id",
                table: "inv_stock_units",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_units_ingredient_id_size_key",
                table: "inv_stock_units",
                columns: new[] { "ingredient_id", "size_key" },
                unique: true);
        }
    }
}
