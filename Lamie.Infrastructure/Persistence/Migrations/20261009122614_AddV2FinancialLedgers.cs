using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddV2FinancialLedgers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "shipping_fee_incurred_at",
                table: "sales_orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "shipping_fee_paid_at",
                table: "sales_orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_fee_payment_idempotency_key",
                table: "sales_orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "shipping_fee_payment_method",
                table: "sales_orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at",
                table: "fin_expenses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "payment_history_incomplete",
                table: "fin_expenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "payment_idempotency_key",
                table: "fin_expenses",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_method",
                table: "fin_expenses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vendor",
                table: "fin_expenses",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            // Existing expense rows predate paid-at tracking. Keep them out of cash-flow
            // totals until a user confirms whether/when money actually left the business.
            migrationBuilder.Sql("UPDATE [fin_expenses] SET [payment_history_incomplete] = 1;");

            migrationBuilder.CreateTable(
                name: "fin_order_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    method = table.Column<int>(type: "int", nullable: false),
                    paid_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_order_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_fin_order_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "sales_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fin_order_payments_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "auth_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_completed_at",
                table: "sales_orders",
                column: "completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_shipping_fee_incurred_at",
                table: "sales_orders",
                column: "shipping_fee_incurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_shipping_fee_paid_at",
                table: "sales_orders",
                column: "shipping_fee_paid_at");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_shipping_fee_payment_idempotency_key",
                table: "sales_orders",
                column: "shipping_fee_payment_idempotency_key",
                unique: true,
                filter: "[shipping_fee_payment_idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fin_expenses_paid_at",
                table: "fin_expenses",
                column: "paid_at");

            migrationBuilder.CreateIndex(
                name: "ix_fin_expenses_payment_idempotency_key",
                table: "fin_expenses",
                column: "payment_idempotency_key",
                unique: true,
                filter: "[payment_idempotency_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fin_order_payments_created_by_id",
                table: "fin_order_payments",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_fin_order_payments_idempotency_key",
                table: "fin_order_payments",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fin_order_payments_order_id_paid_at",
                table: "fin_order_payments",
                columns: new[] { "order_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fin_order_payments_paid_at_status",
                table: "fin_order_payments",
                columns: new[] { "paid_at", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fin_order_payments");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_completed_at",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_shipping_fee_incurred_at",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_shipping_fee_paid_at",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_shipping_fee_payment_idempotency_key",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_fin_expenses_paid_at",
                table: "fin_expenses");

            migrationBuilder.DropIndex(
                name: "ix_fin_expenses_payment_idempotency_key",
                table: "fin_expenses");

            migrationBuilder.DropColumn(
                name: "shipping_fee_incurred_at",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "shipping_fee_paid_at",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "shipping_fee_payment_idempotency_key",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "shipping_fee_payment_method",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "paid_at",
                table: "fin_expenses");

            migrationBuilder.DropColumn(
                name: "payment_history_incomplete",
                table: "fin_expenses");

            migrationBuilder.DropColumn(
                name: "payment_idempotency_key",
                table: "fin_expenses");

            migrationBuilder.DropColumn(
                name: "payment_method",
                table: "fin_expenses");

            migrationBuilder.DropColumn(
                name: "vendor",
                table: "fin_expenses");
        }
    }
}
