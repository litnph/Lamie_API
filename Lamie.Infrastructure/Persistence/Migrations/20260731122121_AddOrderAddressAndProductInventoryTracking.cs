using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAddressAndProductInventoryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'sales_orders', N'delivery_address_description') IS NULL
                    ALTER TABLE [sales_orders]
                    ADD [delivery_address_description] nvarchar(1000) NULL;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "tracks_inventory",
                table: "cat_products",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tracks_inventory",
                table: "cat_products");

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM [__EFMigrationsHistory]
                    WHERE [MigrationId] = N'20260731102859_AddOrderDeliveryAddressDescription'
                ) AND COL_LENGTH(N'sales_orders', N'delivery_address_description') IS NOT NULL
                    ALTER TABLE [sales_orders]
                    DROP COLUMN [delivery_address_description];
                """);
        }
    }
}
