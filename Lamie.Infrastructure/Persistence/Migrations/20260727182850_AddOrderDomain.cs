using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "cat_products",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "crm_customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    normalized_phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    normalized_email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crm_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    channel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    orderer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    orderer_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    recipient_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    recipient_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    pickup_at_shop = table.Column<bool>(type: "bit", nullable: false),
                    delivery_address = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    delivery_latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    delivery_longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    delivery_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    deposit_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    shipping_fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    shipping_fee_actual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    sub_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_status = table.Column<int>(type: "int", nullable: false),
                    order_status = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    content_note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    inventory_reserved = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_orders_auth_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "auth_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_auth_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "auth_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_crm_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "crm_customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_orders_sales_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "sales_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_change_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    entity_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    field_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    old_value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    new_value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    change_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    changed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    changed_by_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    changed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_change_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_order_change_logs_auth_users_changed_by_id",
                        column: x => x.changed_by_id,
                        principalTable: "auth_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_change_logs_sales_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    image_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_order_images_sales_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<int>(type: "int", nullable: true),
                    product_sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    product_name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    thumbnail_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    discount_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_order_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "cat_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_order_items_sales_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_crm_customers_created_at",
                table: "crm_customers",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_crm_customers_normalized_email",
                table: "crm_customers",
                column: "normalized_email",
                filter: "[normalized_email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_crm_customers_normalized_phone",
                table: "crm_customers",
                column: "normalized_phone");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_change_logs_changed_by_id",
                table: "sales_order_change_logs",
                column: "changed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_change_logs_order_id_changed_at",
                table: "sales_order_change_logs",
                columns: new[] { "order_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_images_order_id_sort_order",
                table: "sales_order_images",
                columns: new[] { "order_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_items_order_id",
                table: "sales_order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_items_product_id",
                table: "sales_order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_channel_id_created_at",
                table: "sales_orders",
                columns: new[] { "channel_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_created_at",
                table: "sales_orders",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_created_by_id",
                table: "sales_orders",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_customer_id_created_at",
                table: "sales_orders",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_delivery_at_order_status",
                table: "sales_orders",
                columns: new[] { "delivery_at", "order_status" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_order_code",
                table: "sales_orders",
                column: "order_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_order_status_created_at",
                table: "sales_orders",
                columns: new[] { "order_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_payment_status_created_at",
                table: "sales_orders",
                columns: new[] { "payment_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_updated_by_id",
                table: "sales_orders",
                column: "updated_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_order_change_logs");

            migrationBuilder.DropTable(
                name: "sales_order_images");

            migrationBuilder.DropTable(
                name: "sales_order_items");

            migrationBuilder.DropTable(
                name: "sales_orders");

            migrationBuilder.DropTable(
                name: "crm_customers");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "cat_products");
        }
    }
}
