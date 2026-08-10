using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fin_expense_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    normalized_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fin_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expense_category_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_expenses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fin_expense_categories_normalized_name",
                table: "fin_expense_categories",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fin_expense_categories_sort_order_name",
                table: "fin_expense_categories",
                columns: new[] { "sort_order", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_fin_expenses_expense_category_id",
                table: "fin_expenses",
                column: "expense_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_fin_expenses_expense_date_expense_category_id",
                table: "fin_expenses",
                columns: new[] { "expense_date", "expense_category_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fin_expense_categories");

            migrationBuilder.DropTable(
                name: "fin_expenses");
        }
    }
}
