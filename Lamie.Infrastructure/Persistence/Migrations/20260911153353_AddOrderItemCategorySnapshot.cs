using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemCategorySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "category_id",
                table: "sales_order_items",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category_name",
                table: "sales_order_items",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category_id",
                table: "sales_order_items");

            migrationBuilder.DropColumn(
                name: "category_name",
                table: "sales_order_items");
        }
    }
}
