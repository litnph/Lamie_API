using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataIntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rel_product_tags_product_id",
                table: "rel_product_tags");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_styles_product_id",
                table: "rel_product_styles");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_occasions_product_id",
                table: "rel_product_occasions");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_colors_product_id",
                table: "rel_product_colors");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_collections_product_id",
                table: "rel_product_collections");

            migrationBuilder.DropIndex(
                name: "ix_md_tag_translations_tag_id",
                table: "md_tag_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_style_translations_style_id",
                table: "md_style_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_occasion_translations_occasion_id",
                table: "md_occasion_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_color_translations_color_id",
                table: "md_color_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_collection_translations_collection_id",
                table: "md_collection_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_category_translations_category_id",
                table: "md_category_translations");

            migrationBuilder.DropIndex(
                name: "ix_cat_product_translations_product_id",
                table: "cat_product_translations");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sys_languages",
                table: "sys_languages");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "sys_languages",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_tag_translations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_style_translations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_occasion_translations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_color_translations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_collection_translations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_category_translations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "sku",
                table: "cat_products",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "cat_product_translations",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddPrimaryKey(
                name: "pk_sys_languages",
                table: "sys_languages",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_tags_product_id_tag_id",
                table: "rel_product_tags",
                columns: new[] { "product_id", "tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_tags_tag_id",
                table: "rel_product_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_styles_product_id_style_id",
                table: "rel_product_styles",
                columns: new[] { "product_id", "style_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_styles_style_id",
                table: "rel_product_styles",
                column: "style_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_occasions_occasion_id",
                table: "rel_product_occasions",
                column: "occasion_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_occasions_product_id_occasion_id",
                table: "rel_product_occasions",
                columns: new[] { "product_id", "occasion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_colors_color_id",
                table: "rel_product_colors",
                column: "color_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_colors_product_id_color_id",
                table: "rel_product_colors",
                columns: new[] { "product_id", "color_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_collections_collection_id",
                table: "rel_product_collections",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_collections_product_id_collection_id",
                table: "rel_product_collections",
                columns: new[] { "product_id", "collection_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_tag_translations_language_code",
                table: "md_tag_translations",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_md_tag_translations_tag_id_language_code",
                table: "md_tag_translations",
                columns: new[] { "tag_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_style_translations_language_code",
                table: "md_style_translations",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_md_style_translations_style_id_language_code",
                table: "md_style_translations",
                columns: new[] { "style_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_occasion_translations_language_code",
                table: "md_occasion_translations",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_md_occasion_translations_occasion_id_language_code",
                table: "md_occasion_translations",
                columns: new[] { "occasion_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_color_translations_color_id_language_code",
                table: "md_color_translations",
                columns: new[] { "color_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_color_translations_language_code",
                table: "md_color_translations",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_md_collection_translations_collection_id_language_code",
                table: "md_collection_translations",
                columns: new[] { "collection_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_collection_translations_language_code",
                table: "md_collection_translations",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_md_category_translations_category_id_language_code",
                table: "md_category_translations",
                columns: new[] { "category_id", "language_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_category_translations_language_code",
                table: "md_category_translations",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_cat_products_category_id",
                table: "cat_products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_cat_products_sku",
                table: "cat_products",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cat_product_translations_language_code",
                table: "cat_product_translations",
                column: "language_code");

            migrationBuilder.CreateIndex(
                name: "ix_cat_product_translations_product_id_language_code",
                table: "cat_product_translations",
                columns: new[] { "product_id", "language_code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_cat_product_translations_sys_languages_language_code",
                table: "cat_product_translations",
                column: "language_code",
                principalTable: "sys_languages",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cat_products_categories_category_id",
                table: "cat_products",
                column: "category_id",
                principalTable: "md_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_md_category_translations_sys_languages_language_code",
                table: "md_category_translations",
                column: "language_code",
                principalTable: "sys_languages",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_md_collection_translations_sys_languages_language_code",
                table: "md_collection_translations",
                column: "language_code",
                principalTable: "sys_languages",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_md_color_translations_sys_languages_language_code",
                table: "md_color_translations",
                column: "language_code",
                principalTable: "sys_languages",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_md_occasion_translations_sys_languages_language_code",
                table: "md_occasion_translations",
                column: "language_code",
                principalTable: "sys_languages",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_md_style_translations_sys_languages_language_code",
                table: "md_style_translations",
                column: "language_code",
                principalTable: "sys_languages",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_md_tag_translations_sys_languages_language_code",
                table: "md_tag_translations",
                column: "language_code",
                principalTable: "sys_languages",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rel_product_collections_collections_collection_id",
                table: "rel_product_collections",
                column: "collection_id",
                principalTable: "md_collections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rel_product_colors_colors_color_id",
                table: "rel_product_colors",
                column: "color_id",
                principalTable: "md_colors",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rel_product_occasions_occasions_occasion_id",
                table: "rel_product_occasions",
                column: "occasion_id",
                principalTable: "md_occasions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rel_product_styles_styles_style_id",
                table: "rel_product_styles",
                column: "style_id",
                principalTable: "md_styles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_rel_product_tags_tags_tag_id",
                table: "rel_product_tags",
                column: "tag_id",
                principalTable: "md_tags",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cat_product_translations_sys_languages_language_code",
                table: "cat_product_translations");

            migrationBuilder.DropForeignKey(
                name: "fk_cat_products_categories_category_id",
                table: "cat_products");

            migrationBuilder.DropForeignKey(
                name: "fk_md_category_translations_sys_languages_language_code",
                table: "md_category_translations");

            migrationBuilder.DropForeignKey(
                name: "fk_md_collection_translations_sys_languages_language_code",
                table: "md_collection_translations");

            migrationBuilder.DropForeignKey(
                name: "fk_md_color_translations_sys_languages_language_code",
                table: "md_color_translations");

            migrationBuilder.DropForeignKey(
                name: "fk_md_occasion_translations_sys_languages_language_code",
                table: "md_occasion_translations");

            migrationBuilder.DropForeignKey(
                name: "fk_md_style_translations_sys_languages_language_code",
                table: "md_style_translations");

            migrationBuilder.DropForeignKey(
                name: "fk_md_tag_translations_sys_languages_language_code",
                table: "md_tag_translations");

            migrationBuilder.DropForeignKey(
                name: "fk_rel_product_collections_collections_collection_id",
                table: "rel_product_collections");

            migrationBuilder.DropForeignKey(
                name: "fk_rel_product_colors_colors_color_id",
                table: "rel_product_colors");

            migrationBuilder.DropForeignKey(
                name: "fk_rel_product_occasions_occasions_occasion_id",
                table: "rel_product_occasions");

            migrationBuilder.DropForeignKey(
                name: "fk_rel_product_styles_styles_style_id",
                table: "rel_product_styles");

            migrationBuilder.DropForeignKey(
                name: "fk_rel_product_tags_tags_tag_id",
                table: "rel_product_tags");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_tags_product_id_tag_id",
                table: "rel_product_tags");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_tags_tag_id",
                table: "rel_product_tags");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_styles_product_id_style_id",
                table: "rel_product_styles");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_styles_style_id",
                table: "rel_product_styles");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_occasions_occasion_id",
                table: "rel_product_occasions");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_occasions_product_id_occasion_id",
                table: "rel_product_occasions");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_colors_color_id",
                table: "rel_product_colors");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_colors_product_id_color_id",
                table: "rel_product_colors");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_collections_collection_id",
                table: "rel_product_collections");

            migrationBuilder.DropIndex(
                name: "ix_rel_product_collections_product_id_collection_id",
                table: "rel_product_collections");

            migrationBuilder.DropIndex(
                name: "ix_md_tag_translations_language_code",
                table: "md_tag_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_tag_translations_tag_id_language_code",
                table: "md_tag_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_style_translations_language_code",
                table: "md_style_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_style_translations_style_id_language_code",
                table: "md_style_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_occasion_translations_language_code",
                table: "md_occasion_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_occasion_translations_occasion_id_language_code",
                table: "md_occasion_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_color_translations_color_id_language_code",
                table: "md_color_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_color_translations_language_code",
                table: "md_color_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_collection_translations_collection_id_language_code",
                table: "md_collection_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_collection_translations_language_code",
                table: "md_collection_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_category_translations_category_id_language_code",
                table: "md_category_translations");

            migrationBuilder.DropIndex(
                name: "ix_md_category_translations_language_code",
                table: "md_category_translations");

            migrationBuilder.DropIndex(
                name: "ix_cat_products_category_id",
                table: "cat_products");

            migrationBuilder.DropIndex(
                name: "ix_cat_products_sku",
                table: "cat_products");

            migrationBuilder.DropIndex(
                name: "ix_cat_product_translations_language_code",
                table: "cat_product_translations");

            migrationBuilder.DropIndex(
                name: "ix_cat_product_translations_product_id_language_code",
                table: "cat_product_translations");

            migrationBuilder.DropPrimaryKey(
                name: "pk_sys_languages",
                table: "sys_languages");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "sys_languages",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_tag_translations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_style_translations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_occasion_translations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_color_translations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_collection_translations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "md_category_translations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "sku",
                table: "cat_products",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "language_code",
                table: "cat_product_translations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AddPrimaryKey(
                name: "pk_sys_languages",
                table: "sys_languages",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_tags_product_id",
                table: "rel_product_tags",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_styles_product_id",
                table: "rel_product_styles",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_occasions_product_id",
                table: "rel_product_occasions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_colors_product_id",
                table: "rel_product_colors",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_collections_product_id",
                table: "rel_product_collections",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_tag_translations_tag_id",
                table: "md_tag_translations",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_style_translations_style_id",
                table: "md_style_translations",
                column: "style_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_occasion_translations_occasion_id",
                table: "md_occasion_translations",
                column: "occasion_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_color_translations_color_id",
                table: "md_color_translations",
                column: "color_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_collection_translations_collection_id",
                table: "md_collection_translations",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_category_translations_category_id",
                table: "md_category_translations",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_cat_product_translations_product_id",
                table: "cat_product_translations",
                column: "product_id");
        }
    }
}
