using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNavigationBackend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_navigation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    module_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    page_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    icon_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    permission_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_visible = table.Column<bool>(type: "bit", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    open_in_new_tab = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_navigation", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_navigation_is_enabled_is_visible",
                table: "auth_navigation",
                columns: new[] { "is_enabled", "is_visible" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_navigation_key",
                table: "auth_navigation",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_navigation_module_key_page_key",
                table: "auth_navigation",
                columns: new[] { "module_key", "page_key" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_navigation_parent_id_sort_order_label",
                table: "auth_navigation",
                columns: new[] { "parent_id", "sort_order", "label" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_navigation_permission_code",
                table: "auth_navigation",
                column: "permission_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_navigation");
        }
    }
}
