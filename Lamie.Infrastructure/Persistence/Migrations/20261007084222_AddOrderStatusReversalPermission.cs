using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStatusReversalPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "auth_permissions",
                columns: new[] { "id", "code", "created_at", "description", "group", "is_active", "is_system", "name", "sort_order", "updated_at" },
                values: new object[] { new Guid("10000000-0000-4000-8000-000000000031"), "orders.reverse-status", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Đưa đơn hàng về bước xử lý liền trước.", "Đơn hàng", true, true, "Trả lại trạng thái đơn hàng", 310, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "auth_role_permissions",
                columns: new[] { "permission_id", "role_id", "granted_at" },
                values: new object[] { new Guid("10000000-0000-4000-8000-000000000031"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000031"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000031"));
        }
    }
}
