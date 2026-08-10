using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultAdminNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @seed TABLE
                (
                    id uniqueidentifier NOT NULL,
                    [key] nvarchar(120) NOT NULL,
                    parent_key nvarchar(120) NULL,
                    module_key nvarchar(120) NULL,
                    page_key nvarchar(160) NULL,
                    label nvarchar(160) NOT NULL,
                    [path] nvarchar(400) NULL,
                    icon_key nvarchar(80) NULL,
                    permission_code nvarchar(120) NULL,
                    sort_order int NOT NULL,
                    is_visible bit NOT NULL
                );

                INSERT INTO @seed
                    (id, [key], parent_key, module_key, page_key, label, [path], icon_key, permission_code, sort_order, is_visible)
                VALUES
                    ('90000000-0000-4000-8000-000000000001', N'group.system', NULL, NULL, NULL, N'Hệ thống', NULL, N'folder', NULL, 200, 1),
                    ('90000000-0000-4000-8000-000000000002', N'users.list', N'group.system', N'users', N'users.list', N'Người dùng', N'/admin/users', N'users', N'users.view', 10, 1),
                    ('90000000-0000-4000-8000-000000000003', N'users.create', N'users.list', N'users', N'users.create', N'Tạo người dùng', N'/admin/users/new', NULL, N'users.manage', 10, 0),
                    ('90000000-0000-4000-8000-000000000004', N'users.edit', N'users.list', N'users', N'users.edit', N'Chỉnh sửa người dùng', N'/admin/users/:id/edit', NULL, N'users.manage', 20, 0),
                    ('90000000-0000-4000-8000-000000000005', N'settings.channels', N'group.management', N'settings-channels', N'settings.channels', N'Kênh bán', N'/admin/settings/channels', N'radio', N'channels.view', 20, 1),
                    ('90000000-0000-4000-8000-000000000006', N'settings.attributes', N'group.management', N'settings-attributes', N'settings.attributes', N'Thuộc tính', N'/admin/settings/attributes/categories', N'settings', N'settings.view', 30, 1),
                    ('90000000-0000-4000-8000-000000000007', N'settings.attributes.route', N'settings.attributes', N'settings-attributes', N'settings.attributes', N'Thuộc tính theo nhóm', N'/admin/settings/attributes/:attributeKey', NULL, N'settings.view', 10, 0),
                    ('90000000-0000-4000-8000-000000000008', N'roles.list', N'group.system', N'roles', N'roles.list', N'Vai trò & quyền', N'/admin/roles', N'shield-check', N'roles.view', 20, 1),
                    ('90000000-0000-4000-8000-000000000009', N'reports.financial', NULL, N'reports', N'reports.financial', N'Báo cáo', N'/admin/reports', N'chart-no-axes-combined', N'reports.view', 60, 1),
                    ('90000000-0000-4000-8000-000000000010', N'expenses.list', NULL, N'expenses', N'expenses.list', N'Chi phí', N'/admin/expenses', N'receipt-text', N'expenses.view', 50, 1),
                    ('90000000-0000-4000-8000-000000000011', N'expenses.categories', N'expenses.list', N'expenses', N'expenses.categories', N'Danh mục chi phí', N'/admin/settings/expense-categories', NULL, N'expenses.view', 10, 0),
                    ('90000000-0000-4000-8000-000000000012', N'group.management', NULL, NULL, NULL, N'Quản lý', NULL, N'folder', NULL, 100, 1),
                    ('90000000-0000-4000-8000-000000000013', N'products.list', N'group.management', N'products', N'products.list', N'Sản phẩm', N'/admin/products', N'flower-2', N'products.view', 10, 1),
                    ('90000000-0000-4000-8000-000000000014', N'products.create', N'products.list', N'products', N'products.create', N'Tạo sản phẩm', N'/admin/products/create', NULL, N'products.manage', 10, 0),
                    ('90000000-0000-4000-8000-000000000015', N'products.edit', N'products.list', N'products', N'products.edit', N'Chỉnh sửa sản phẩm', N'/admin/products/:id/edit', NULL, N'products.manage', 20, 0),
                    ('90000000-0000-4000-8000-000000000016', N'dashboard.home', NULL, N'dashboard', N'dashboard.home', N'Tổng quan', N'/admin/dashboard', N'layout-dashboard', N'dashboard.view', 10, 1),
                    ('90000000-0000-4000-8000-000000000017', N'orders.list', NULL, N'orders', N'orders.list', N'Đơn hàng', N'/admin/orders', N'shopping-bag', N'orders.view', 20, 1),
                    ('90000000-0000-4000-8000-000000000018', N'orders.calendar', NULL, N'orders', N'orders.calendar', N'Lịch giao', N'/admin/orders/calendar', N'calendar-days', N'orders.view', 40, 1),
                    ('90000000-0000-4000-8000-000000000019', N'orders.create', N'orders.list', N'orders', N'orders.create', N'Tạo đơn hàng', N'/admin/orders/new', NULL, N'orders.manage', 10, 0),
                    ('90000000-0000-4000-8000-000000000020', N'orders.detail', N'orders.list', N'orders', N'orders.detail', N'Chi tiết đơn hàng', N'/admin/orders/:id', NULL, N'orders.view', 20, 0),
                    ('90000000-0000-4000-8000-000000000021', N'orders.edit', N'orders.list', N'orders', N'orders.edit', N'Chỉnh sửa đơn hàng', N'/admin/orders/:id/edit', NULL, N'orders.manage', 30, 0),
                    ('90000000-0000-4000-8000-000000000022', N'customers.list', NULL, N'customers', N'customers.list', N'Khách hàng', N'/admin/customers', N'contact-round', N'customers.view', 30, 1),
                    ('90000000-0000-4000-8000-000000000023', N'customers.detail', N'customers.list', N'customers', N'customers.detail', N'Chi tiết khách hàng', N'/admin/customers/:id', NULL, N'customers.view', 10, 0),
                    ('90000000-0000-4000-8000-000000000024', N'permissions.list', N'group.system', N'access-control', N'permissions.list', N'Quyền hạn', N'/admin/permissions', N'key-round', N'roles.view', 30, 1),
                    ('90000000-0000-4000-8000-000000000025', N'navigation.manage', N'group.system', N'access-control', N'navigation.manage', N'Menu & Điều hướng', N'/admin/navigation', N'list-tree', N'navigation.view', 40, 1);

                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT s.id, s.[key], NULL, s.module_key, s.page_key, s.label, NULL, s.[path], s.icon_key,
                       s.permission_code, s.sort_order, s.is_visible, 1, 1, 0, @now, @now, NULL, NULL
                FROM @seed s
                WHERE s.parent_key IS NULL
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation n WHERE n.[key] = s.[key])
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation n WHERE n.id = s.id);

                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT s.id, s.[key], p.id, s.module_key, s.page_key, s.label, NULL, s.[path], s.icon_key,
                       s.permission_code, s.sort_order, s.is_visible, 1, 1, 0, @now, @now, NULL, NULL
                FROM @seed s
                INNER JOIN auth_navigation p ON p.[key] = s.parent_key
                WHERE s.parent_key IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation n WHERE n.[key] = s.[key])
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation n WHERE n.id = s.id);

                INSERT INTO auth_navigation
                    (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                     permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                     created_at, updated_at, created_by, updated_by)
                SELECT s.id, s.[key], p.id, s.module_key, s.page_key, s.label, NULL, s.[path], s.icon_key,
                       s.permission_code, s.sort_order, s.is_visible, 1, 1, 0, @now, @now, NULL, NULL
                FROM @seed s
                INNER JOIN auth_navigation p ON p.[key] = s.parent_key
                WHERE s.parent_key IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation n WHERE n.[key] = s.[key])
                  AND NOT EXISTS (SELECT 1 FROM auth_navigation n WHERE n.id = s.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE n
                FROM auth_navigation n
                WHERE n.id IN
                (
                    '90000000-0000-4000-8000-000000000001', '90000000-0000-4000-8000-000000000002',
                    '90000000-0000-4000-8000-000000000003', '90000000-0000-4000-8000-000000000004',
                    '90000000-0000-4000-8000-000000000005', '90000000-0000-4000-8000-000000000006',
                    '90000000-0000-4000-8000-000000000007', '90000000-0000-4000-8000-000000000008',
                    '90000000-0000-4000-8000-000000000009', '90000000-0000-4000-8000-000000000010',
                    '90000000-0000-4000-8000-000000000011', '90000000-0000-4000-8000-000000000012',
                    '90000000-0000-4000-8000-000000000013', '90000000-0000-4000-8000-000000000014',
                    '90000000-0000-4000-8000-000000000015', '90000000-0000-4000-8000-000000000016',
                    '90000000-0000-4000-8000-000000000017', '90000000-0000-4000-8000-000000000018',
                    '90000000-0000-4000-8000-000000000019', '90000000-0000-4000-8000-000000000020',
                    '90000000-0000-4000-8000-000000000021', '90000000-0000-4000-8000-000000000022',
                    '90000000-0000-4000-8000-000000000023', '90000000-0000-4000-8000-000000000024',
                    '90000000-0000-4000-8000-000000000025'
                )
                AND n.[key] IN
                (
                    N'group.system', N'users.list', N'users.create', N'users.edit', N'settings.channels',
                    N'settings.attributes', N'settings.attributes.route', N'roles.list', N'reports.financial',
                    N'expenses.list', N'expenses.categories', N'group.management', N'products.list',
                    N'products.create', N'products.edit', N'dashboard.home', N'orders.list', N'orders.calendar',
                    N'orders.create', N'orders.detail', N'orders.edit', N'customers.list', N'customers.detail',
                    N'permissions.list', N'navigation.manage'
                )
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM auth_navigation child
                    WHERE child.parent_id = n.id
                      AND child.id NOT IN
                      (
                          '90000000-0000-4000-8000-000000000001', '90000000-0000-4000-8000-000000000002',
                          '90000000-0000-4000-8000-000000000003', '90000000-0000-4000-8000-000000000004',
                          '90000000-0000-4000-8000-000000000005', '90000000-0000-4000-8000-000000000006',
                          '90000000-0000-4000-8000-000000000007', '90000000-0000-4000-8000-000000000008',
                          '90000000-0000-4000-8000-000000000009', '90000000-0000-4000-8000-000000000010',
                          '90000000-0000-4000-8000-000000000011', '90000000-0000-4000-8000-000000000012',
                          '90000000-0000-4000-8000-000000000013', '90000000-0000-4000-8000-000000000014',
                          '90000000-0000-4000-8000-000000000015', '90000000-0000-4000-8000-000000000016',
                          '90000000-0000-4000-8000-000000000017', '90000000-0000-4000-8000-000000000018',
                          '90000000-0000-4000-8000-000000000019', '90000000-0000-4000-8000-000000000020',
                          '90000000-0000-4000-8000-000000000021', '90000000-0000-4000-8000-000000000022',
                          '90000000-0000-4000-8000-000000000023', '90000000-0000-4000-8000-000000000024',
                          '90000000-0000-4000-8000-000000000025'
                      )
                );
                """);
        }
    }
}
