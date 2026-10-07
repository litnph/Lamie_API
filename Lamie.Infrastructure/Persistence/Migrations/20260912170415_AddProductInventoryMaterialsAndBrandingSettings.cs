using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductInventoryMaterialsAndBrandingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "shop_logo_url",
                table: "cat_product_settings",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "watermark_enabled",
                table: "cat_product_settings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "watermark_image_url",
                table: "cat_product_settings",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "watermark_source",
                table: "cat_product_settings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "rel_product_inventory_materials",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<int>(type: "int", nullable: false),
                    stock_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rel_product_inventory_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_rel_product_inventory_materials_cat_products_product_id",
                        column: x => x.product_id,
                        principalTable: "cat_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "cat_product_settings",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "shop_logo_url", "watermark_enabled", "watermark_image_url", "watermark_source" },
                values: new object[] { null, true, null, 1 });

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_inventory_materials_product_id_stock_unit_id",
                table: "rel_product_inventory_materials",
                columns: new[] { "product_id", "stock_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_inventory_materials_stock_unit_id",
                table: "rel_product_inventory_materials",
                column: "stock_unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rel_product_inventory_materials");

            migrationBuilder.DropColumn(
                name: "shop_logo_url",
                table: "cat_product_settings");

            migrationBuilder.DropColumn(
                name: "watermark_enabled",
                table: "cat_product_settings");

            migrationBuilder.DropColumn(
                name: "watermark_image_url",
                table: "cat_product_settings");

            migrationBuilder.DropColumn(
                name: "watermark_source",
                table: "cat_product_settings");
        }
    }
}
