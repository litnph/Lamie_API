using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    icon_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_channels", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "sales_channels",
                columns: new[] { "id", "code", "created_at", "icon_url", "is_active", "name", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "admin", new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Admin", 10, new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "website", new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Website", 20, new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "phone", new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Phone", 30, new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("44444444-4444-4444-4444-444444444444"), "walk-in", new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Walk-in", 40, new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("55555555-5555-5555-5555-555555555555"), "social", new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Social", 50, new DateTime(2026, 7, 28, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_channels_code",
                table: "sales_channels",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_channels_sort_order_name",
                table: "sales_channels",
                columns: new[] { "sort_order", "name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_channels");
        }
    }
}
