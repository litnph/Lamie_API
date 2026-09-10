using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    public partial class AddOrderItemProductTypeSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "product_type_id",
                table: "sales_order_items",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_type_name",
                table: "sales_order_items",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "product_type_id", table: "sales_order_items");
            migrationBuilder.DropColumn(name: "product_type_name", table: "sales_order_items");
        }
    }
}
