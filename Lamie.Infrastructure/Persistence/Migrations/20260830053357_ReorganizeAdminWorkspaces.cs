using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReorganizeAdminWorkspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "auth_permissions",
                columns: new[] { "id", "code", "created_at", "description", "group", "is_active", "is_system", "name", "sort_order", "updated_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000029"), "tasks.view", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Mở không gian Việc cần làm; dữ liệu từng tab vẫn yêu cầu quyền nghiệp vụ tương ứng.", "Vận hành", true, true, "Xem Việc cần làm", 290, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000030"), "catalog-settings.view", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Mở không gian Cài đặt danh mục; từng nhóm dữ liệu vẫn yêu cầu quyền riêng.", "Cấu hình", true, true, "Xem Cài đặt danh mục", 300, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "auth_role_permissions",
                columns: new[] { "permission_id", "role_id", "granted_at" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-4000-8000-000000000029"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000030"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000029"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000030"), new Guid("20000000-0000-4000-8000-000000000002"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000029"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-4000-8000-000000000030"), new Guid("20000000-0000-4000-8000-000000000003"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @tasks_permission uniqueidentifier = '10000000-0000-4000-8000-000000000029';
                DECLARE @catalog_permission uniqueidentifier = '10000000-0000-4000-8000-000000000030';

                INSERT INTO auth_role_permissions (role_id, permission_id, granted_at)
                SELECT DISTINCT grant_source.role_id, @tasks_permission, @now
                FROM auth_role_permissions grant_source
                INNER JOIN auth_permissions permission ON permission.id = grant_source.permission_id
                WHERE permission.code IN (N'ingredient-reports.view', N'inventory.view')
                  AND NOT EXISTS (
                      SELECT 1 FROM auth_role_permissions existing
                      WHERE existing.role_id = grant_source.role_id
                        AND existing.permission_id = @tasks_permission);

                INSERT INTO auth_role_permissions (role_id, permission_id, granted_at)
                SELECT DISTINCT grant_source.role_id, @catalog_permission, @now
                FROM auth_role_permissions grant_source
                INNER JOIN auth_permissions permission ON permission.id = grant_source.permission_id
                WHERE permission.code IN (N'ingredients.view', N'inventory.view', N'settings.view', N'channels.view')
                  AND NOT EXISTS (
                      SELECT 1 FROM auth_role_permissions existing
                      WHERE existing.role_id = grant_source.role_id
                        AND existing.permission_id = @catalog_permission);

                DECLARE @seed TABLE
                (
                    id uniqueidentifier NOT NULL,
                    [key] nvarchar(120) NOT NULL,
                    parent_key nvarchar(120) NULL,
                    module_key nvarchar(120) NULL,
                    page_key nvarchar(160) NULL,
                    label nvarchar(160) NOT NULL,
                    [path] nvarchar(400) NULL,
                    icon_key nvarchar(100) NULL,
                    permission_code nvarchar(120) NULL,
                    sort_order int NOT NULL,
                    is_visible bit NOT NULL
                );

                INSERT INTO @seed VALUES
                    ('90000000-0000-4000-8000-000000000040', N'group.sales', NULL, NULL, NULL, N'BÁN HÀNG', NULL, N'folder', NULL, 20, 1),
                    ('90000000-0000-4000-8000-000000000041', N'group.operations', NULL, NULL, NULL, N'VẬN HÀNH', NULL, N'folder', NULL, 30, 1),
                    ('90000000-0000-4000-8000-000000000042', N'group.finance', NULL, NULL, NULL, N'TÀI CHÍNH & BÁO CÁO', NULL, N'folder', NULL, 40, 1),
                    ('90000000-0000-4000-8000-000000000043', N'group.settings', NULL, NULL, NULL, N'CÀI ĐẶT', NULL, N'folder', NULL, 70, 1),
                    ('90000000-0000-4000-8000-000000000044', N'inventory.group', N'group.operations', NULL, NULL, N'Kho hàng', NULL, N'package-open', NULL, 20, 1),
                    ('90000000-0000-4000-8000-000000000045', N'tasks.workspace', N'group.operations', N'ingredients', N'ingredients.tasks', N'Việc cần làm', N'/admin/tasks', N'list-checks', N'tasks.view', 10, 1),
                    ('90000000-0000-4000-8000-000000000046', N'catalog-settings.home', N'group.settings', N'catalog-settings', N'catalog-settings.home', N'Cài đặt danh mục', N'/admin/settings/catalog', N'settings', N'catalog-settings.view', 10, 1),
                    ('90000000-0000-4000-8000-000000000047', N'ingredients.inventory-receiving', N'inventory.group', N'ingredients', N'ingredients.inventory-receiving', N'Nhập kho', N'/admin/inventory/receiving', N'package-plus', N'inventory.manage', 20, 1),
                    ('90000000-0000-4000-8000-000000000048', N'ingredients.inventory-transactions', N'inventory.group', N'ingredients', N'ingredients.inventory-transactions', N'Lịch sử giao dịch', N'/admin/inventory/transactions', N'history', N'inventory.view', 30, 1),
                    ('90000000-0000-4000-8000-000000000049', N'ingredients.inventory-catalog', N'catalog-settings.home', N'ingredients', N'ingredients.inventory-catalog', N'Danh mục hàng tồn kho', N'/admin/settings/catalog/inventory', N'boxes', N'inventory.view', 110, 0);

                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT seed.id, seed.[key], NULL, seed.module_key, seed.page_key, seed.label, NULL, seed.[path], seed.icon_key,
                       seed.permission_code, seed.sort_order, seed.is_visible, 1, 1, 0, @now, @now, NULL, NULL
                FROM @seed seed
                WHERE seed.parent_key IS NULL
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.[key] = seed.[key])
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.id = seed.id);

                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT seed.id, seed.[key], parent.id, seed.module_key, seed.page_key, seed.label, NULL, seed.[path], seed.icon_key,
                       seed.permission_code, seed.sort_order, seed.is_visible, 1, 1, 0, @now, @now, NULL, NULL
                FROM @seed seed
                INNER JOIN auth_navigation parent ON parent.[key] = seed.parent_key
                WHERE seed.parent_key IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.[key] = seed.[key])
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.id = seed.id);

                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT seed.id, seed.[key], parent.id, seed.module_key, seed.page_key, seed.label, NULL, seed.[path], seed.icon_key,
                       seed.permission_code, seed.sort_order, seed.is_visible, 1, 1, 0, @now, @now, NULL, NULL
                FROM @seed seed
                INNER JOIN auth_navigation parent ON parent.[key] = seed.parent_key
                WHERE seed.parent_key IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.[key] = seed.[key])
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation existing WHERE existing.id = seed.id);

                UPDATE node SET parent_id = parent.id, sort_order = mapping.sort_order,
                    label = mapping.label, is_visible = mapping.is_visible, is_enabled = 1, updated_at = @now
                FROM auth_navigation node
                INNER JOIN (VALUES
                    (N'orders.list', N'group.sales', 10, N'Đơn hàng', CAST(1 AS bit)),
                    (N'orders.calendar', N'group.sales', 20, N'Lịch giao', CAST(1 AS bit)),
                    (N'customers.list', N'group.sales', 30, N'Khách hàng', CAST(1 AS bit)),
                    (N'expenses.list', N'group.finance', 10, N'Chi phí', CAST(1 AS bit)),
                    (N'reports.financial', N'group.finance', 20, N'Báo cáo', CAST(1 AS bit)),
                    (N'ingredients.inventory', N'inventory.group', 10, N'Tồn kho hiện tại', CAST(1 AS bit)),
                    (N'ingredients.demand', N'tasks.workspace', 10, N'Nguyên liệu cần chuẩn bị', CAST(0 AS bit)),
                    (N'ingredients.list', N'catalog-settings.home', 100, N'Danh mục nguyên liệu', CAST(0 AS bit)),
                    (N'settings.channels', N'catalog-settings.home', 120, N'Kênh bán', CAST(0 AS bit)),
                    (N'settings.attributes', N'catalog-settings.home', 130, N'Thuộc tính', CAST(0 AS bit))
                ) mapping([key], parent_key, sort_order, label, is_visible) ON mapping.[key] = node.[key]
                INNER JOIN auth_navigation parent ON parent.[key] = mapping.parent_key
                WHERE node.is_system = 1;

                UPDATE auth_navigation SET parent_id = NULL, sort_order = 10, label = N'Tổng quan', updated_at = @now WHERE [key] = N'dashboard.home' AND is_system = 1;
                UPDATE auth_navigation SET parent_id = NULL, sort_order = 50, label = N'Sản phẩm', updated_at = @now WHERE [key] = N'products.list' AND is_system = 1;
                UPDATE auth_navigation SET parent_id = NULL, sort_order = 60, label = N'Nội dung', updated_at = @now WHERE [key] = N'content.create' AND is_system = 1;
                UPDATE auth_navigation SET is_visible = 0, updated_at = @now WHERE [key] IN (N'content.footer', N'content.history', N'ingredients.units') AND is_system = 1;
                UPDATE auth_navigation SET parent_id = NULL, sort_order = 80, label = N'HỆ THỐNG', updated_at = @now WHERE [key] = N'group.system' AND is_system = 1;
                UPDATE auth_navigation SET label = N'Vai trò', updated_at = @now WHERE [key] = N'roles.list' AND is_system = 1;
                UPDATE auth_navigation SET is_visible = 0, updated_at = @now WHERE [key] = N'group.management' AND is_system = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();

                UPDATE node SET parent_id = parent.id, sort_order = mapping.sort_order,
                    label = mapping.label, is_visible = mapping.is_visible, updated_at = @now
                FROM auth_navigation node
                INNER JOIN (VALUES
                    (N'products.list', N'group.management', 10, N'Sản phẩm', CAST(1 AS bit)),
                    (N'settings.channels', N'group.management', 20, N'Kênh bán', CAST(1 AS bit)),
                    (N'settings.attributes', N'group.management', 30, N'Thuộc tính', CAST(1 AS bit)),
                    (N'ingredients.list', N'group.management', 40, N'Danh mục chuẩn bị', CAST(1 AS bit)),
                    (N'ingredients.inventory', N'group.management', 45, N'Kho tồn', CAST(1 AS bit)),
                    (N'content.create', N'group.management', 50, N'Content', CAST(1 AS bit)),
                    (N'ingredients.demand', N'ingredients.list', 30, N'Nguyên liệu cần chuẩn bị', CAST(1 AS bit))
                ) mapping([key], parent_key, sort_order, label, is_visible) ON mapping.[key] = node.[key]
                INNER JOIN auth_navigation parent ON parent.[key] = mapping.parent_key
                WHERE node.is_system = 1;

                UPDATE auth_navigation SET parent_id = NULL, sort_order = 20, updated_at = @now WHERE [key] = N'orders.list' AND is_system = 1;
                UPDATE auth_navigation SET parent_id = NULL, sort_order = 30, updated_at = @now WHERE [key] = N'customers.list' AND is_system = 1;
                UPDATE auth_navigation SET parent_id = NULL, sort_order = 40, updated_at = @now WHERE [key] = N'orders.calendar' AND is_system = 1;
                UPDATE auth_navigation SET parent_id = NULL, sort_order = 50, updated_at = @now WHERE [key] = N'expenses.list' AND is_system = 1;
                UPDATE auth_navigation SET parent_id = NULL, sort_order = 60, updated_at = @now WHERE [key] = N'reports.financial' AND is_system = 1;
                UPDATE auth_navigation SET is_visible = 1, updated_at = @now WHERE [key] IN (N'content.footer', N'content.history', N'ingredients.units', N'group.management') AND is_system = 1;
                UPDATE auth_navigation SET label = N'Hệ thống', sort_order = 200, updated_at = @now WHERE [key] = N'group.system' AND is_system = 1;
                UPDATE auth_navigation SET label = N'Vai trò & quyền', updated_at = @now WHERE [key] = N'roles.list' AND is_system = 1;

                DELETE FROM auth_navigation
                WHERE [key] IN (
                    N'ingredients.inventory-catalog', N'ingredients.inventory-transactions',
                    N'ingredients.inventory-receiving', N'catalog-settings.home');
                DELETE FROM auth_navigation WHERE [key] IN (N'tasks.workspace', N'inventory.group');
                DELETE FROM auth_navigation WHERE [key] IN (N'group.settings', N'group.finance', N'group.operations', N'group.sales');

                DELETE FROM auth_role_permissions
                WHERE permission_id IN (
                    '10000000-0000-4000-8000-000000000029',
                    '10000000-0000-4000-8000-000000000030');
                """);

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000029"));

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000030"));

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000029"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000030"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000029"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000030"), new Guid("20000000-0000-4000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000029"), new Guid("20000000-0000-4000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000030"), new Guid("20000000-0000-4000-8000-000000000003") });
        }
    }
}
