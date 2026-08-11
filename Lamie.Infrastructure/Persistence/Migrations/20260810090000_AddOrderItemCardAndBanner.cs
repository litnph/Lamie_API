using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260810090000_AddOrderItemCardAndBanner")]
public partial class AddOrderItemCardAndBanner : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "banner_message", table: "sales_order_items", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "card_message", table: "sales_order_items", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<bool>(name: "has_banner", table: "sales_order_items", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "has_card", table: "sales_order_items", type: "bit", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "banner_message", table: "sales_order_items");
        migrationBuilder.DropColumn(name: "card_message", table: "sales_order_items");
        migrationBuilder.DropColumn(name: "has_banner", table: "sales_order_items");
        migrationBuilder.DropColumn(name: "has_card", table: "sales_order_items");
    }
}
