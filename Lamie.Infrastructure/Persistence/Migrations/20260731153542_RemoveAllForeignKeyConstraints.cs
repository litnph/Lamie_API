using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAllForeignKeyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @dropForeignKeys nvarchar(max) = N'';

                SELECT @dropForeignKeys = @dropForeignKeys
                    + N'ALTER TABLE '
                    + QUOTENAME(SCHEMA_NAME(parent_table.schema_id))
                    + N'.'
                    + QUOTENAME(parent_table.name)
                    + N' DROP CONSTRAINT '
                    + QUOTENAME(foreign_key.name)
                    + N';'
                    + CHAR(13)
                    + CHAR(10)
                FROM sys.foreign_keys AS foreign_key
                INNER JOIN sys.tables AS parent_table
                    ON parent_table.object_id = foreign_key.parent_object_id
                ORDER BY foreign_key.name;

                IF LEN(@dropForeignKeys) > 0
                    EXEC sys.sp_executesql @dropForeignKeys;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible. Recreating database foreign keys would
            // violate the application's foreign-key-free persistence contract.
        }
    }
}
