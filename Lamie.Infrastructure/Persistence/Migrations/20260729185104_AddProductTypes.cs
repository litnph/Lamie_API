using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "product_type_id",
                table: "cat_products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "md_product_types",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_by = table.Column<int>(type: "int", nullable: true),
                    created_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<int>(type: "int", nullable: true),
                    updated_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_product_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "md_product_type_translations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_type_id = table.Column<int>(type: "int", nullable: false),
                    language_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_by = table.Column<int>(type: "int", nullable: true),
                    created_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by = table.Column<int>(type: "int", nullable: true),
                    updated_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_product_type_translations", x => x.id);
                    table.ForeignKey(
                        name: "fk_md_product_type_translations_md_product_types_product_type_id",
                        column: x => x.product_type_id,
                        principalTable: "md_product_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_md_product_type_translations_sys_languages_language_code",
                        column: x => x.language_code,
                        principalTable: "sys_languages",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cat_products_product_type_id",
                table: "cat_products",
                column: "product_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_product_type_translations_language_code",
                table: "md_product_type_translations",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_md_product_type_translations_product_type_id_language_code",
                table: "md_product_type_translations",
                columns: new[] { "product_type_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_product_types_code",
                table: "md_product_types",
                column: "code",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO md_product_types (code, sort_order, is_active, created_at, updated_at)
                SELECT 'FRESH_FLOWER', 10, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
                WHERE NOT EXISTS (SELECT 1 FROM md_product_types WHERE code = 'FRESH_FLOWER');

                INSERT INTO md_product_types (code, sort_order, is_active, created_at, updated_at)
                SELECT 'WAX_FLOWER', 20, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
                WHERE NOT EXISTS (SELECT 1 FROM md_product_types WHERE code = 'WAX_FLOWER');

                IF EXISTS (SELECT 1 FROM sys_languages WHERE code = 'vi')
                BEGIN
                    INSERT INTO md_product_type_translations
                        (product_type_id, language_code, name, description, created_at, updated_at)
                    SELECT id, 'vi', N'Hoa tươi', N'Sản phẩm được thiết kế chủ yếu từ hoa tươi.', SYSUTCDATETIME(), SYSUTCDATETIME()
                    FROM md_product_types product_type
                    WHERE product_type.code = 'FRESH_FLOWER'
                      AND NOT EXISTS (
                          SELECT 1 FROM md_product_type_translations translation
                          WHERE translation.product_type_id = product_type.id AND translation.language_code = 'vi');

                    INSERT INTO md_product_type_translations
                        (product_type_id, language_code, name, description, created_at, updated_at)
                    SELECT id, 'vi', N'Hoa sáp', N'Sản phẩm được thiết kế chủ yếu từ hoa sáp.', SYSUTCDATETIME(), SYSUTCDATETIME()
                    FROM md_product_types product_type
                    WHERE product_type.code = 'WAX_FLOWER'
                      AND NOT EXISTS (
                          SELECT 1 FROM md_product_type_translations translation
                          WHERE translation.product_type_id = product_type.id AND translation.language_code = 'vi');
                END
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_cat_products_product_types_product_type_id",
                table: "cat_products",
                column: "product_type_id",
                principalTable: "md_product_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cat_products_product_types_product_type_id",
                table: "cat_products");

            migrationBuilder.DropTable(
                name: "md_product_type_translations");

            migrationBuilder.DropTable(
                name: "md_product_types");

            migrationBuilder.DropIndex(
                name: "ix_cat_products_product_type_id",
                table: "cat_products");

            migrationBuilder.DropColumn(
                name: "product_type_id",
                table: "cat_products");
        }
    }
}
