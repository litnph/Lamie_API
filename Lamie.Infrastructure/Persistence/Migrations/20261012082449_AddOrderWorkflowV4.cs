using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderWorkflowV4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "recipient_phone",
                table: "sales_orders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "recipient_name",
                table: "sales_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<Guid>(
                name: "assignee_id",
                table: "sales_orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assignee_name",
                table: "sales_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancelled_reason",
                table: "sales_orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "card_prepared",
                table: "sales_orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "delivered_at",
                table: "sales_orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "design_confirmed",
                table: "sales_orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "hold_at",
                table: "sales_orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "hold_by_id",
                table: "sales_orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                table: "sales_orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "materials_prepared",
                table: "sales_orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "paid_in_full_at",
                table: "sales_orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "preparation_date",
                table: "sales_orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "quality_checked",
                table: "sales_orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // A legacy Completed row proves only that the old workflow considered the
            // order delivered. Keep the new Completed meaning only when both delivery
            // evidence and sufficient successful payment events exist. No missing
            // recipient or payment timestamp is synthesized.
            migrationBuilder.Sql(
                """
                UPDATE sales_orders
                SET recipient_name = NULL
                WHERE LTRIM(RTRIM(recipient_name)) = '';

                UPDATE sales_orders
                SET recipient_phone = NULL
                WHERE LTRIM(RTRIM(recipient_phone)) = '';

                UPDATE sales_orders
                SET delivered_at = completed_at
                WHERE order_status = 4 AND completed_at IS NOT NULL;

                ;WITH payment_evidence AS
                (
                    SELECT
                        order_id,
                        SUM(CASE WHEN kind = 4 THEN -amount ELSE amount END) AS net_paid,
                        MAX(paid_at) AS last_paid_at
                    FROM fin_order_payments
                    WHERE status = 1
                    GROUP BY order_id
                )
                UPDATE orders
                SET paid_in_full_at = evidence.last_paid_at
                FROM sales_orders AS orders
                INNER JOIN payment_evidence AS evidence ON evidence.order_id = orders.id
                WHERE orders.order_status = 4
                  AND evidence.net_paid >= orders.total_amount;

                UPDATE orders
                SET order_status = 7,
                    completed_at = NULL
                FROM sales_orders AS orders
                WHERE orders.order_status = 4
                  AND (
                      orders.delivered_at IS NULL
                      OR orders.paid_in_full_at IS NULL
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_delivered_at",
                table: "sales_orders",
                column: "delivered_at");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_paid_in_full_at",
                table: "sales_orders",
                column: "paid_in_full_at");

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_preparation_date",
                table: "sales_orders",
                column: "preparation_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_orders_delivered_at",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_paid_in_full_at",
                table: "sales_orders");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_preparation_date",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "assignee_name",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "cancelled_reason",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "card_prepared",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "design_confirmed",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "hold_at",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "hold_by_id",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "materials_prepared",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "paid_in_full_at",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "preparation_date",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "quality_checked",
                table: "sales_orders");

            migrationBuilder.AlterColumn<string>(
                name: "recipient_phone",
                table: "sales_orders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "recipient_name",
                table: "sales_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
