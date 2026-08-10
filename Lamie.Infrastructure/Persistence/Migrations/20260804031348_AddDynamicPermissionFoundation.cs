using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicPermissionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_auth_permissions_group_name",
                table: "auth_permissions");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "auth_permissions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "auth_permissions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_system",
                table: "auth_permissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "auth_permissions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "auth_permissions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateTable(
                name: "auth_access_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_access_audit", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000001"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 10, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000002"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 20, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000003"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 30, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000004"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 40, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000005"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 50, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000006"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 60, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000007"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 70, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000008"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 80, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000009"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 90, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000010"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 100, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000011"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 110, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000012"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 120, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000013"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 130, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000014"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 140, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000015"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 150, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000016"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 160, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000017"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 170, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000018"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 180, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000019"),
                columns: new[] { "created_at", "is_active", "is_system", "sort_order", "updated_at" },
                values: new object[] { new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 190, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            // Seed additively: an installation may already contain an operator-created row
            // with the same code or reserved id. Never overwrite or delete that row.
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM [auth_permissions]
                    WHERE [id] = '10000000-0000-4000-8000-000000000020'
                       OR [code] = N'navigation.view')
                BEGIN
                    INSERT INTO [auth_permissions]
                        ([id], [code], [created_at], [description], [group], [is_active], [is_system], [name], [sort_order], [updated_at])
                    VALUES
                        ('10000000-0000-4000-8000-000000000020', N'navigation.view', '2026-08-03T00:00:00',
                         N'Xem cấu hình menu và route quản trị.', N'Phân quyền', 1, 1,
                         N'Xem menu và điều hướng', 200, '2026-08-03T00:00:00');
                END;

                IF NOT EXISTS (
                    SELECT 1 FROM [auth_permissions]
                    WHERE [id] = '10000000-0000-4000-8000-000000000021'
                       OR [code] = N'navigation.manage')
                BEGIN
                    INSERT INTO [auth_permissions]
                        ([id], [code], [created_at], [description], [group], [is_active], [is_system], [name], [sort_order], [updated_at])
                    VALUES
                        ('10000000-0000-4000-8000-000000000021', N'navigation.manage', '2026-08-03T00:00:00',
                         N'Tạo, sắp xếp, bật, tắt và ẩn menu quản trị.', N'Phân quyền', 1, 1,
                         N'Quản lý menu và điều hướng', 210, '2026-08-03T00:00:00');
                END;

                INSERT INTO [auth_role_permissions] ([role_id], [permission_id], [granted_at])
                SELECT '20000000-0000-4000-8000-000000000001', permission.[id], '2026-08-03T00:00:00'
                FROM [auth_permissions] AS permission
                WHERE permission.[code] IN (N'navigation.view', N'navigation.manage')
                  AND EXISTS (
                      SELECT 1 FROM [auth_roles]
                      WHERE [id] = '20000000-0000-4000-8000-000000000001')
                  AND NOT EXISTS (
                      SELECT 1 FROM [auth_role_permissions] AS role_permission
                      WHERE role_permission.[role_id] = '20000000-0000-4000-8000-000000000001'
                        AND role_permission.[permission_id] = permission.[id]);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_auth_permissions_is_active_group_sort_order_name",
                table: "auth_permissions",
                columns: new[] { "is_active", "group", "sort_order", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_access_audit_actor_user_id_occurred_at",
                table: "auth_access_audit",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_access_audit_entity_type_entity_id_occurred_at",
                table: "auth_access_audit",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_access_audit");

            migrationBuilder.DropIndex(
                name: "ix_auth_permissions_is_active_group_sort_order_name",
                table: "auth_permissions");

            migrationBuilder.Sql(
                """
                DELETE role_permission
                FROM [auth_role_permissions] AS role_permission
                INNER JOIN [auth_permissions] AS permission ON permission.[id] = role_permission.[permission_id]
                WHERE role_permission.[role_id] = '20000000-0000-4000-8000-000000000001'
                  AND permission.[id] IN (
                      '10000000-0000-4000-8000-000000000020',
                      '10000000-0000-4000-8000-000000000021')
                  AND permission.[code] IN (N'navigation.view', N'navigation.manage')
                  AND permission.[is_system] = 1;

                DELETE FROM [auth_permissions]
                WHERE [id] IN (
                    '10000000-0000-4000-8000-000000000020',
                    '10000000-0000-4000-8000-000000000021')
                  AND [code] IN (N'navigation.view', N'navigation.manage')
                  AND [is_system] = 1;
                """);

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "auth_permissions");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "auth_permissions");

            migrationBuilder.DropColumn(
                name: "is_system",
                table: "auth_permissions");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "auth_permissions");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "auth_permissions");

            migrationBuilder.CreateIndex(
                name: "ix_auth_permissions_group_name",
                table: "auth_permissions",
                columns: new[] { "group", "name" });
        }
    }
}
