using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReorganizeAdminSidebar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();

                UPDATE navigation
                SET sort_order = navigation.sort_order + 100000,
                    updated_at = @now
                FROM auth_navigation navigation
                WHERE navigation.is_system = 1
                  AND (navigation.parent_id IS NULL
                   OR navigation.parent_id IN (
                     SELECT id FROM auth_navigation
                     WHERE [key] IN (N'group.sales', N'group.operations', N'group.goods',
                                     N'group.finance', N'group.settings', N'group.system')));

                IF EXISTS (SELECT 1 FROM auth_navigation WHERE [key] = N'group.goods' AND is_system = 0)
                    THROW 51000, 'Cannot create system navigation group.goods because the key is already operator-owned.', 1;

                IF NOT EXISTS (SELECT 1 FROM auth_navigation WHERE [key] = N'group.goods')
                BEGIN
                    INSERT INTO auth_navigation
                        (id, [key], parent_id, module_key, page_key, label, description, [path], icon_key,
                         permission_code, sort_order, is_visible, is_enabled, is_system, open_in_new_tab,
                         created_at, updated_at, created_by, updated_by)
                    VALUES
                        ('90000000-0000-4000-8000-000000000054', N'group.goods', NULL, NULL, NULL,
                         N'Sản phẩm & kho', N'Quản lý sản phẩm và hàng tồn kho.', NULL, NULL, NULL,
                         40, 1, 1, 1, 0, @now, @now, NULL, NULL);
                END;

                UPDATE navigation
                SET parent_id = NULL,
                    sort_order = grouping.sort_order,
                    label = grouping.label,
                    description = grouping.description,
                    icon_key = NULL,
                    is_visible = 1,
                    is_enabled = 1,
                    updated_at = @now
                FROM auth_navigation navigation
                INNER JOIN (VALUES
                    (N'group.sales', 20, N'Bán hàng', N'Quản lý đơn hàng và khách hàng.'),
                    (N'group.operations', 30, N'Vận hành', N'Theo dõi lịch giao và công việc cần xử lý.'),
                    (N'group.goods', 40, N'Sản phẩm & kho', N'Quản lý sản phẩm và hàng tồn kho.'),
                    (N'group.finance', 50, N'Tài chính', N'Theo dõi báo cáo tài chính và chi phí.'),
                    (N'group.settings', 70, N'Quản trị', N'Cấu hình cửa hàng, người dùng và quyền truy cập.')
                ) grouping([key], sort_order, label, description) ON grouping.[key] = navigation.[key]
                WHERE navigation.is_system = 1;

                UPDATE navigation
                SET parent_id = parent.id,
                    sort_order = mapping.sort_order,
                    label = mapping.label,
                    updated_at = @now
                FROM auth_navigation navigation
                INNER JOIN (VALUES
                    (N'orders.list', N'group.sales', 10, N'Đơn hàng'),
                    (N'customers.list', N'group.sales', 20, N'Khách hàng'),
                    (N'orders.calendar', N'group.operations', 10, N'Lịch giao'),
                    (N'tasks.workspace', N'group.operations', 20, N'Việc cần làm'),
                    (N'products.list', N'group.goods', 10, N'Sản phẩm'),
                    (N'ingredients.inventory', N'group.goods', 20, N'Kho hàng'),
                    (N'reports.financial', N'group.finance', 10, N'Báo cáo tài chính'),
                    (N'expenses.list', N'group.finance', 20, N'Chi phí'),
                    (N'catalog-settings.home', N'group.settings', 10, N'Cấu hình cửa hàng'),
                    (N'users.list', N'group.settings', 20, N'Người dùng'),
                    (N'roles.list', N'group.settings', 30, N'Vai trò'),
                    (N'permissions.list', N'group.settings', 40, N'Quyền hạn'),
                    (N'navigation.manage', N'group.settings', 50, N'Menu & điều hướng')
                ) mapping([key], parent_key, sort_order, label) ON mapping.[key] = navigation.[key]
                INNER JOIN auth_navigation parent ON parent.[key] = mapping.parent_key
                WHERE navigation.is_system = 1;

                UPDATE auth_navigation
                SET parent_id = NULL, sort_order = 10, label = N'Tổng quan', updated_at = @now
                WHERE [key] = N'dashboard.home' AND is_system = 1;

                UPDATE auth_navigation
                SET parent_id = NULL, sort_order = 60, label = N'Nội dung đa kênh', updated_at = @now
                WHERE [key] = N'content.create' AND is_system = 1;

                UPDATE auth_navigation
                SET parent_id = NULL, sort_order = 900, label = N'Nhóm hệ thống cũ',
                    is_visible = 0, updated_at = @now
                WHERE [key] = N'group.system' AND is_system = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @now datetime2 = SYSUTCDATETIME();

                UPDATE navigation
                SET sort_order = navigation.sort_order + 100000,
                    updated_at = @now
                FROM auth_navigation navigation
                WHERE navigation.is_system = 1
                  AND (navigation.parent_id IS NULL
                   OR navigation.parent_id IN (
                     SELECT id FROM auth_navigation
                     WHERE [key] IN (N'group.sales', N'group.operations', N'group.goods',
                                     N'group.finance', N'group.settings', N'group.system')));

                UPDATE navigation
                SET parent_id = parent.id,
                    sort_order = mapping.sort_order,
                    label = mapping.label,
                    updated_at = @now
                FROM auth_navigation navigation
                INNER JOIN (VALUES
                    (N'orders.list', N'group.sales', 10, N'Đơn hàng'),
                    (N'orders.calendar', N'group.sales', 20, N'Lịch giao'),
                    (N'customers.list', N'group.sales', 30, N'Khách hàng'),
                    (N'tasks.workspace', N'group.operations', 10, N'Việc cần làm'),
                    (N'ingredients.inventory', N'group.operations', 20, N'Kho hàng'),
                    (N'expenses.list', N'group.finance', 10, N'Chi phí'),
                    (N'reports.financial', N'group.finance', 20, N'Báo cáo'),
                    (N'catalog-settings.home', N'group.settings', 10, N'Cài đặt danh mục'),
                    (N'users.list', N'group.system', 10, N'Người dùng'),
                    (N'roles.list', N'group.system', 20, N'Vai trò'),
                    (N'permissions.list', N'group.system', 30, N'Quyền hạn'),
                    (N'navigation.manage', N'group.system', 40, N'Menu & Điều hướng')
                ) mapping([key], parent_key, sort_order, label) ON mapping.[key] = navigation.[key]
                INNER JOIN auth_navigation parent ON parent.[key] = mapping.parent_key
                WHERE navigation.is_system = 1;

                UPDATE navigation
                SET parent_id = NULL,
                    sort_order = grouping.sort_order,
                    label = grouping.label,
                    description = NULL,
                    icon_key = N'folder',
                    is_visible = 1,
                    updated_at = @now
                FROM auth_navigation navigation
                INNER JOIN (VALUES
                    (N'group.sales', 20, N'BÁN HÀNG'),
                    (N'group.operations', 30, N'VẬN HÀNH'),
                    (N'group.finance', 40, N'TÀI CHÍNH & BÁO CÁO'),
                    (N'group.settings', 70, N'CÀI ĐẶT'),
                    (N'group.system', 80, N'HỆ THỐNG')
                ) grouping([key], sort_order, label) ON grouping.[key] = navigation.[key]
                WHERE navigation.is_system = 1;

                UPDATE auth_navigation
                SET parent_id = NULL, sort_order = 10, label = N'Tổng quan', updated_at = @now
                WHERE [key] = N'dashboard.home' AND is_system = 1;

                UPDATE auth_navigation
                SET parent_id = NULL, sort_order = 50, label = N'Sản phẩm', updated_at = @now
                WHERE [key] = N'products.list' AND is_system = 1;

                UPDATE auth_navigation
                SET parent_id = NULL, sort_order = 60, label = N'Nội dung', updated_at = @now
                WHERE [key] = N'content.create' AND is_system = 1;

                DELETE FROM auth_navigation
                WHERE [key] = N'group.goods' AND is_system = 1;
                """);
        }
    }
}
