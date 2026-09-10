using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductDiscoveryFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cat_product_images_product_id",
                table: "cat_product_images");

            migrationBuilder.AddColumn<byte[]>(
                name: "thumbnail_visual_embedding",
                table: "cat_products",
                type: "varbinary(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_visual_embedding_version",
                table: "cat_products",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "visual_embedding",
                table: "cat_product_images",
                type: "varbinary(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "visual_embedding_version",
                table: "cat_product_images",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cat_product_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    price_deviation_percent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cat_product_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rel_product_similar_products",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<int>(type: "int", nullable: false),
                    similar_product_id = table.Column<int>(type: "int", nullable: false),
                    created_by = table.Column<int>(type: "int", nullable: true),
                    created_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<int>(type: "int", nullable: true),
                    updated_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rel_product_similar_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_rel_product_similar_products_cat_products_product_id",
                        column: x => x.product_id,
                        principalTable: "cat_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rel_product_similar_products_cat_products_similar_product_id",
                        column: x => x.similar_product_id,
                        principalTable: "cat_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "cat_product_settings",
                columns: new[] { "id", "price_deviation_percent", "updated_at" },
                values: new object[] { 1, 20m, new DateTime(2026, 9, 4, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @parent uniqueidentifier = (SELECT TOP (1) id FROM auth_navigation WHERE [key] = N'products.list');

                IF @parent IS NOT NULL AND NOT EXISTS (SELECT 1 FROM auth_navigation WHERE [key] = N'products.recognition')
                BEGIN
                    INSERT INTO auth_navigation
                        (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                         permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                         created_at, updated_at, created_by, updated_by)
                    VALUES
                        ('90000000-0000-4000-8000-000000000050', N'products.recognition', @parent,
                         N'products', N'products.recognition', N'Nhận diện bằng ảnh', NULL,
                         N'/admin/products/recognition', NULL, N'products.manage', 30, 0, 1, 1, 0,
                         @now, @now, NULL, NULL);
                END;

                IF @parent IS NOT NULL AND NOT EXISTS (SELECT 1 FROM auth_navigation WHERE [key] = N'products.catalog-settings')
                BEGIN
                    INSERT INTO auth_navigation
                        (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                         permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                         created_at, updated_at, created_by, updated_by)
                    VALUES
                        ('90000000-0000-4000-8000-000000000051', N'products.catalog-settings', @parent,
                         N'products', N'products.catalog-settings', N'Cài đặt gợi ý theo giá', NULL,
                         N'/admin/products/catalog-settings', NULL, N'products.manage', 40, 0, 1, 1, 0,
                         @now, @now, NULL, NULL);
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_cat_product_images_product_id_is_active",
                table: "cat_product_images",
                columns: new[] { "product_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_similar_products_product_id_similar_product_id",
                table: "rel_product_similar_products",
                columns: new[] { "product_id", "similar_product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_similar_products_similar_product_id",
                table: "rel_product_similar_products",
                column: "similar_product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM auth_navigation
                WHERE id IN (
                    '90000000-0000-4000-8000-000000000050',
                    '90000000-0000-4000-8000-000000000051')
                  AND is_system = 1;
                """);

            migrationBuilder.DropTable(
                name: "cat_product_settings");

            migrationBuilder.DropTable(
                name: "rel_product_similar_products");

            migrationBuilder.DropIndex(
                name: "ix_cat_product_images_product_id_is_active",
                table: "cat_product_images");

            migrationBuilder.DropColumn(
                name: "thumbnail_visual_embedding",
                table: "cat_products");

            migrationBuilder.DropColumn(
                name: "thumbnail_visual_embedding_version",
                table: "cat_products");

            migrationBuilder.DropColumn(
                name: "visual_embedding",
                table: "cat_product_images");

            migrationBuilder.DropColumn(
                name: "visual_embedding_version",
                table: "cat_product_images");

            migrationBuilder.CreateIndex(
                name: "ix_cat_product_images_product_id",
                table: "cat_product_images",
                column: "product_id");
        }
    }
}
