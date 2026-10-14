using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecognizedRevenueRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "affects_recognized_revenue",
                table: "fin_order_payments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "product_revenue_adjustment",
                table: "fin_order_payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "shipping_revenue_adjustment",
                table: "fin_order_payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "affects_recognized_revenue",
                table: "fin_order_payments");

            migrationBuilder.DropColumn(
                name: "product_revenue_adjustment",
                table: "fin_order_payments");

            migrationBuilder.DropColumn(
                name: "shipping_revenue_adjustment",
                table: "fin_order_payments");
        }
    }
}
