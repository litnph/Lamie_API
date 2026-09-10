using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientPlanningAndContentPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ingredient_snapshot_captured_at_utc",
                table: "sales_order_items",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_visible_on_fe",
                table: "cat_products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Preserve the existing storefront catalog on first deployment. New products remain opt-in.
            migrationBuilder.Sql("UPDATE cat_products SET is_visible_on_fe = 1 WHERE is_active = 1;");

            migrationBuilder.CreateTable(
                name: "content_footer_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<int>(type: "int", nullable: false),
                    content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    hashtags = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_footer_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "content_generations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<int>(type: "int", nullable: false),
                    product_id = table.Column<int>(type: "int", nullable: true),
                    product_name_snapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    product_image_url_snapshot = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    brief = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    style_id = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    style_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    style_seed = table.Column<int>(type: "int", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    prompt_version = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    request_fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    parent_generation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    saved_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_generations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "md_measurement_units",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    allows_fractional = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_measurement_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "content_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    public_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_assets_content_generations_generation_id",
                        column: x => x.generation_id,
                        principalTable: "content_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<int>(type: "int", nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    footer_snapshot = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    hashtags_snapshot = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    full_content = table.Column<string>(type: "nvarchar(max)", maxLength: 14000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_content_items_content_generations_generation_id",
                        column: x => x.generation_id,
                        principalTable: "content_generations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "md_ingredients",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    base_unit_id = table.Column<int>(type: "int", nullable: false),
                    note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_ingredients", x => x.id);
                    table.ForeignKey(
                        name: "fk_md_ingredients_md_measurement_units_base_unit_id",
                        column: x => x.base_unit_id,
                        principalTable: "md_measurement_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "md_ingredient_conversions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ingredient_id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    unit_id = table.Column<int>(type: "int", nullable: false),
                    factor_to_base = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_md_ingredient_conversions", x => x.id);
                    table.ForeignKey(
                        name: "fk_md_ingredient_conversions_md_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "md_ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_md_ingredient_conversions_md_measurement_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "md_measurement_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rel_product_ingredients",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    product_id = table.Column<int>(type: "int", nullable: false),
                    ingredient_id = table.Column<int>(type: "int", nullable: false),
                    base_quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rel_product_ingredients", x => x.id);
                    table.ForeignKey(
                        name: "fk_rel_product_ingredients_cat_products_product_id",
                        column: x => x.product_id,
                        principalTable: "cat_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rel_product_ingredients_md_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "md_ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_item_ingredient_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ingredient_id = table.Column<int>(type: "int", nullable: true),
                    ingredient_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ingredient_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    base_unit_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    base_unit_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    base_unit_symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    per_product_base_quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    product_quantity = table.Column<int>(type: "int", nullable: false),
                    total_base_quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    captured_at_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_item_ingredient_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_order_item_ingredient_snapshots_ingredients_ingredient_id",
                        column: x => x.ingredient_id,
                        principalTable: "md_ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_item_ingredient_snapshots_sales_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalTable: "sales_order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "auth_permissions",
                columns: new[] { "id", "code", "created_at", "description", "group", "is_active", "is_system", "name", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000022"), "ingredients.view", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Xem đơn vị tính, nguyên liệu, định mức và báo cáo nhu cầu.", "Nguyên liệu", true, true, "Xem nguyên liệu", 220, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000023"), "ingredients.manage", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Tạo, cập nhật và ngừng sử dụng đơn vị tính, nguyên liệu và quy cách.", "Nguyên liệu", true, true, "Quản lý nguyên liệu", 230, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000024"), "content.view", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Xem không gian làm việc và lịch sử content bán hàng.", "Content", true, true, "Xem content", 240, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000025"), "content.manage", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Tạo, chỉnh sửa và lưu content bán hàng.", "Content", true, true, "Quản lý content", 250, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000026"), "ingredient-reports.view", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Xem nhu cầu nguyên liệu theo ngày giao và trạng thái đơn hàng.", "Nguyên liệu", true, true, "Xem báo cáo nguyên liệu", 260, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "auth_role_permissions",
                columns: new[] { "permission_id", "role_id", "granted_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000022"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000023"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000024"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000025"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000026"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000022"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000023"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000024"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000025"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000026"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000022"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000024"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000026"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_cat_products_is_visible_on_fe_is_active",
                table: "cat_products",
                columns: new[] { "is_visible_on_fe", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_content_assets_generation_id_sort_order",
                table: "content_assets",
                columns: new[] { "generation_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_footer_settings_is_active_platform",
                table: "content_footer_settings",
                columns: new[] { "is_active", "platform" });

            migrationBuilder.CreateIndex(
                name: "ix_content_footer_settings_platform",
                table: "content_footer_settings",
                column: "platform",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_generations_created_by_idempotency_key",
                table: "content_generations",
                columns: new[] { "created_by", "idempotency_key" },
                unique: true,
                filter: "[idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_content_generations_parent_generation_id",
                table: "content_generations",
                column: "parent_generation_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_generations_parent_generation_id_request_fingerprint",
                table: "content_generations",
                columns: new[] { "parent_generation_id", "request_fingerprint" },
                unique: true,
                filter: "[parent_generation_id] IS NOT NULL AND [request_fingerprint] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_content_generations_product_id_created_at",
                table: "content_generations",
                columns: new[] { "product_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_content_generations_status_created_at",
                table: "content_generations",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_content_items_generation_id_platform",
                table: "content_items",
                columns: new[] { "generation_id", "platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_items_platform",
                table: "content_items",
                column: "platform");

            migrationBuilder.CreateIndex(
                name: "ix_md_ingredient_conversions_ingredient_id_code",
                table: "md_ingredient_conversions",
                columns: new[] { "ingredient_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_ingredient_conversions_ingredient_id_factor_to_base",
                table: "md_ingredient_conversions",
                columns: new[] { "ingredient_id", "factor_to_base" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_ingredient_conversions_ingredient_id_name",
                table: "md_ingredient_conversions",
                columns: new[] { "ingredient_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_ingredient_conversions_ingredient_id_sort_order",
                table: "md_ingredient_conversions",
                columns: new[] { "ingredient_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_md_ingredient_conversions_unit_id",
                table: "md_ingredient_conversions",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_ingredients_base_unit_id",
                table: "md_ingredients",
                column: "base_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_md_ingredients_code",
                table: "md_ingredients",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_ingredients_is_active_name",
                table: "md_ingredients",
                columns: new[] { "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_md_measurement_units_code",
                table: "md_measurement_units",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_md_measurement_units_is_active_name",
                table: "md_measurement_units",
                columns: new[] { "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_ingredients_ingredient_id",
                table: "rel_product_ingredients",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_rel_product_ingredients_product_id_ingredient_id",
                table: "rel_product_ingredients",
                columns: new[] { "product_id", "ingredient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_item_ingredient_snapshots_ingredient_id",
                table: "sales_order_item_ingredient_snapshots",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_item_ingredient_snapshots_order_item_id_sort_order",
                table: "sales_order_item_ingredient_snapshots",
                columns: new[] { "order_item_id", "sort_order" });

            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();

                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT seed.id, seed.[key], NULL, seed.module_key, seed.page_key, seed.label, NULL,
                       seed.[path], seed.icon_key, seed.permission_code, seed.sort_order,
                       1, 1, 1, 0, @now, @now, NULL, NULL
                FROM (VALUES
                    (CAST('90000000-0000-4000-8000-000000000026' AS uniqueidentifier), N'ingredients.list', N'ingredients', N'ingredients.list', N'Nguyên liệu', N'/admin/ingredients', N'flower-2', N'ingredients.view', 70),
                    (CAST('90000000-0000-4000-8000-000000000029' AS uniqueidentifier), N'content.create', N'content', N'content.create', N'Content', N'/admin/content', N'radio', N'content.view', 80)
                ) seed(id, [key], module_key, page_key, label, [path], icon_key, permission_code, sort_order)
                WHERE NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.[key] = seed.[key])
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.id = seed.id);

                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT seed.id, seed.[key], parent.id, seed.module_key, seed.page_key, seed.label, NULL,
                       seed.[path], seed.icon_key, seed.permission_code, seed.sort_order,
                       1, 1, 1, 0, @now, @now, NULL, NULL
                FROM (VALUES
                    (CAST('90000000-0000-4000-8000-000000000027' AS uniqueidentifier), N'ingredients.units', N'ingredients.list', N'ingredients', N'ingredients.units', N'Đơn vị tính', N'/admin/ingredients/units', N'settings', N'ingredients.view', 10),
                    (CAST('90000000-0000-4000-8000-000000000028' AS uniqueidentifier), N'ingredients.demand', N'ingredients.list', N'ingredients', N'ingredients.demand', N'Nhu cầu nguyên liệu', N'/admin/ingredients/demand', N'chart-no-axes-combined', N'ingredient-reports.view', 20),
                    (CAST('90000000-0000-4000-8000-000000000030' AS uniqueidentifier), N'content.footer', N'content.create', N'content', N'content.footer', N'Footer nền tảng', N'/admin/content/footer', N'settings', N'content.manage', 10),
                    (CAST('90000000-0000-4000-8000-000000000031' AS uniqueidentifier), N'content.history', N'content.create', N'content', N'content.history', N'Lịch sử content', N'/admin/content/history', N'list-tree', N'content.view', 20)
                ) seed(id, [key], parent_key, module_key, page_key, label, [path], icon_key, permission_code, sort_order)
                INNER JOIN auth_navigation parent ON parent.[key] = seed.parent_key
                WHERE NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.[key] = seed.[key])
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.id = seed.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM auth_navigation
                WHERE id IN
                (
                    '90000000-0000-4000-8000-000000000027',
                    '90000000-0000-4000-8000-000000000028',
                    '90000000-0000-4000-8000-000000000030',
                    '90000000-0000-4000-8000-000000000031'
                )
                AND [key] IN (N'ingredients.units', N'ingredients.demand', N'content.footer', N'content.history');

                DELETE parent
                FROM auth_navigation parent
                WHERE parent.id IN
                (
                    '90000000-0000-4000-8000-000000000026',
                    '90000000-0000-4000-8000-000000000029'
                )
                AND parent.[key] IN (N'ingredients.list', N'content.create')
                AND NOT EXISTS (SELECT 1 FROM auth_navigation child WHERE child.parent_id = parent.id);
                """);

            migrationBuilder.DropTable(
                name: "content_assets");

            migrationBuilder.DropTable(
                name: "content_footer_settings");

            migrationBuilder.DropTable(
                name: "content_items");

            migrationBuilder.DropTable(
                name: "md_ingredient_conversions");

            migrationBuilder.DropTable(
                name: "rel_product_ingredients");

            migrationBuilder.DropTable(
                name: "sales_order_item_ingredient_snapshots");

            migrationBuilder.DropTable(
                name: "content_generations");

            migrationBuilder.DropTable(
                name: "md_ingredients");

            migrationBuilder.DropTable(
                name: "md_measurement_units");

            migrationBuilder.DropIndex(
                name: "ix_cat_products_is_visible_on_fe_is_active",
                table: "cat_products");

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000022"));

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000023"));

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000024"));

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000025"));

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000026"));

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000022"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000023"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000024"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000025"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000026"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000022"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000023"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000024"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000025"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000026"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000022"), new Guid("20000000-0000-4000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000024"), new Guid("20000000-0000-4000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000026"), new Guid("20000000-0000-4000-8000-000000000003") });

            migrationBuilder.DropColumn(
                name: "ingredient_snapshot_captured_at_utc",
                table: "sales_order_items");

            migrationBuilder.DropColumn(
                name: "is_visible_on_fe",
                table: "cat_products");
        }
    }
}
