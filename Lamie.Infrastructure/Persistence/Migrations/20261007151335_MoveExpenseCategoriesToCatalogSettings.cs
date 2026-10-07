using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveExpenseCategoriesToCatalogSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @catalogSettingsId uniqueidentifier = (
                    SELECT id FROM auth_navigation WHERE [key] = N'catalog-settings.home');

                IF @catalogSettingsId IS NULL
                    THROW 51000, 'Cannot move expenses.categories because catalog-settings.home does not exist.', 1;

                UPDATE auth_navigation
                SET parent_id = @catalogSettingsId,
                    sort_order = 140,
                    updated_at = @now
                WHERE [key] = N'expenses.categories'
                  AND is_system = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @expensesId uniqueidentifier = (
                    SELECT id FROM auth_navigation WHERE [key] = N'expenses.list');

                IF @expensesId IS NULL
                    THROW 51000, 'Cannot restore expenses.categories because expenses.list does not exist.', 1;

                UPDATE auth_navigation
                SET parent_id = @expensesId,
                    sort_order = 10,
                    updated_at = @now
                WHERE [key] = N'expenses.categories'
                  AND is_system = 1;
                """);
        }
    }
}
