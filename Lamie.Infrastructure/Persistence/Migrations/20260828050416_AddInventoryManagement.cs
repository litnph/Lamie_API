using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "stock_receipt_id",
                table: "fin_expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inv_stock_receipt_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stock_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_stock_receipt_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inv_stock_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    receipt_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    client_request_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    received_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    invoice_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    invoice_file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    invoice_content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_stock_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inv_stock_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stock_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<int>(type: "int", nullable: false),
                    quantity_delta = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    balance_after = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    operation_key = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    receipt_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    order_material_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_stock_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inv_stock_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ingredient_id = table.Column<int>(type: "int", nullable: false),
                    size_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    size_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    low_stock_threshold = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inv_stock_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stock_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ingredient_id = table.Column<int>(type: "int", nullable: false),
                    ingredient_code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ingredient_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    size_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    unit_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    unit_symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    required_quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    deducted_quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_materials", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fin_expenses_stock_receipt_id",
                table: "fin_expenses",
                column: "stock_receipt_id",
                unique: true,
                filter: "[stock_receipt_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_receipt_items_receipt_id_stock_unit_id",
                table: "inv_stock_receipt_items",
                columns: new[] { "receipt_id", "stock_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_receipt_items_stock_unit_id",
                table: "inv_stock_receipt_items",
                column: "stock_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_receipts_client_request_id",
                table: "inv_stock_receipts",
                column: "client_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_receipts_receipt_number",
                table: "inv_stock_receipts",
                column: "receipt_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_receipts_received_date_created_at",
                table: "inv_stock_receipts",
                columns: new[] { "received_date", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_transactions_operation_key",
                table: "inv_stock_transactions",
                column: "operation_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_transactions_order_id_created_at",
                table: "inv_stock_transactions",
                columns: new[] { "order_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_transactions_receipt_id",
                table: "inv_stock_transactions",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_transactions_stock_unit_id_created_at",
                table: "inv_stock_transactions",
                columns: new[] { "stock_unit_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_units_ingredient_id_size_key",
                table: "inv_stock_units",
                columns: new[] { "ingredient_id", "size_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_stock_units_is_active_quantity_low_stock_threshold",
                table: "inv_stock_units",
                columns: new[] { "is_active", "quantity", "low_stock_threshold" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_materials_order_id_stock_unit_id",
                table: "sales_order_materials",
                columns: new[] { "order_id", "stock_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_materials_stock_unit_id",
                table: "sales_order_materials",
                column: "stock_unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inv_stock_receipt_items");

            migrationBuilder.DropTable(
                name: "inv_stock_receipts");

            migrationBuilder.DropTable(
                name: "inv_stock_transactions");

            migrationBuilder.DropTable(
                name: "inv_stock_units");

            migrationBuilder.DropTable(
                name: "sales_order_materials");

            migrationBuilder.DropIndex(
                name: "ix_fin_expenses_stock_receipt_id",
                table: "fin_expenses");

            migrationBuilder.DropColumn(
                name: "stock_receipt_id",
                table: "fin_expenses");
        }
    }
}
