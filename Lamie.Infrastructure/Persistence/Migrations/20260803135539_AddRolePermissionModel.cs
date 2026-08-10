using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePermissionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    group = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "auth_role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    granted_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_role_permissions", x => new { x.role_id, x.permission_id });
                });

            migrationBuilder.CreateTable(
                name: "auth_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "auth_user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_user_roles", x => new { x.user_id, x.role_id });
                });

            migrationBuilder.InsertData(
                table: "auth_permissions",
                columns: new[] { "id", "code", "description", "group", "name" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000001"), "products.view", "Xem danh sách và chi tiết sản phẩm.", "Sản phẩm", "Xem sản phẩm" },
                    { new Guid("10000000-0000-4000-8000-000000000002"), "products.manage", "Tạo và cập nhật sản phẩm.", "Sản phẩm", "Quản lý sản phẩm" },
                    { new Guid("10000000-0000-4000-8000-000000000003"), "orders.view", "Xem danh sách và chi tiết đơn hàng.", "Đơn hàng", "Xem đơn hàng" },
                    { new Guid("10000000-0000-4000-8000-000000000004"), "orders.manage", "Tạo và cập nhật đơn hàng.", "Đơn hàng", "Quản lý đơn hàng" },
                    { new Guid("10000000-0000-4000-8000-000000000005"), "orders.cancel", "Hủy đơn hàng đang xử lý.", "Đơn hàng", "Hủy đơn hàng" },
                    { new Guid("10000000-0000-4000-8000-000000000006"), "customers.view", "Xem thông tin khách hàng.", "Khách hàng", "Xem khách hàng" },
                    { new Guid("10000000-0000-4000-8000-000000000007"), "customers.manage", "Cập nhật thông tin khách hàng.", "Khách hàng", "Quản lý khách hàng" },
                    { new Guid("10000000-0000-4000-8000-000000000008"), "channels.view", "Xem danh sách kênh bán.", "Cấu hình", "Xem kênh bán" },
                    { new Guid("10000000-0000-4000-8000-000000000009"), "channels.manage", "Tạo và cập nhật kênh bán.", "Cấu hình", "Quản lý kênh bán" },
                    { new Guid("10000000-0000-4000-8000-000000000010"), "dashboard.view", "Xem màn hình tổng quan vận hành.", "Báo cáo", "Xem tổng quan" },
                    { new Guid("10000000-0000-4000-8000-000000000011"), "settings.view", "Xem dữ liệu cấu hình hệ thống.", "Cấu hình", "Xem cấu hình" },
                    { new Guid("10000000-0000-4000-8000-000000000012"), "settings.manage", "Thay đổi dữ liệu cấu hình hệ thống.", "Cấu hình", "Quản lý cấu hình" },
                    { new Guid("10000000-0000-4000-8000-000000000013"), "expenses.view", "Xem danh mục và các khoản chi.", "Tài chính", "Xem chi phí" },
                    { new Guid("10000000-0000-4000-8000-000000000014"), "expenses.manage", "Tạo, cập nhật và xóa chi phí.", "Tài chính", "Quản lý chi phí" },
                    { new Guid("10000000-0000-4000-8000-000000000015"), "reports.view", "Xem và xuất báo cáo tài chính.", "Báo cáo", "Xem báo cáo" },
                    { new Guid("10000000-0000-4000-8000-000000000016"), "users.view", "Xem tài khoản quản trị.", "Phân quyền", "Xem người dùng" },
                    { new Guid("10000000-0000-4000-8000-000000000017"), "users.manage", "Tạo, cập nhật và khóa tài khoản.", "Phân quyền", "Quản lý người dùng" },
                    { new Guid("10000000-0000-4000-8000-000000000018"), "roles.view", "Xem vai trò và quyền được cấp.", "Phân quyền", "Xem vai trò" },
                    { new Guid("10000000-0000-4000-8000-000000000019"), "roles.manage", "Tạo, cập nhật và xóa vai trò tùy chỉnh.", "Phân quyền", "Quản lý vai trò" }
                });

            migrationBuilder.InsertData(
                table: "auth_role_permissions",
                columns: new[] { "permission_id", "role_id", "granted_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000001"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000002"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000003"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000004"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000005"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000006"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000007"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000008"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000009"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000010"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000011"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000012"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000013"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000014"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000015"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000016"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000017"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000018"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000019"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000001"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000002"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000003"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000004"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000005"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000006"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000007"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000008"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000009"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000010"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000011"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000012"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000013"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000014"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000015"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000001"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000003"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000004"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000005"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000006"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000008"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000010"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000011"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000013"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000015"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "auth_roles",
                columns: new[] { "id", "code", "created_at", "description", "is_active", "is_system", "name", "updated_at" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-4000-8000-000000000001"), "admin", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Toàn quyền quản trị hệ thống.", true, true, "Quản trị viên", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("20000000-0000-4000-8000-000000000002"), "manager", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Quản lý vận hành, cấu hình, chi phí và báo cáo.", true, true, "Quản lý", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("20000000-0000-4000-8000-000000000003"), "staff", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Xử lý nghiệp vụ hàng ngày với quyền quản lý giới hạn.", true, true, "Nhân viên", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [auth_user_roles] ([user_id], [role_id], [assigned_at])
                SELECT
                    [id],
                    CASE [role]
                        WHEN 1 THEN '20000000-0000-4000-8000-000000000001'
                        WHEN 2 THEN '20000000-0000-4000-8000-000000000002'
                        ELSE '20000000-0000-4000-8000-000000000003'
                    END,
                    SYSUTCDATETIME()
                FROM [auth_users];
                """);

            migrationBuilder.CreateIndex(
                name: "ix_auth_permissions_code",
                table: "auth_permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_permissions_group_name",
                table: "auth_permissions",
                columns: new[] { "group", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_role_permissions_permission_id",
                table: "auth_role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_roles_code",
                table: "auth_roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_roles_is_active_name",
                table: "auth_roles",
                columns: new[] { "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_user_roles_role_id",
                table: "auth_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_user_roles_user_id",
                table: "auth_user_roles",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_permissions");

            migrationBuilder.DropTable(
                name: "auth_role_permissions");

            migrationBuilder.DropTable(
                name: "auth_roles");

            migrationBuilder.DropTable(
                name: "auth_user_roles");
        }
    }
}
