using Lamie.Domain.Entities;

namespace Lamie.Application.Identity;

public static class PermissionNames
{
    public const string ProductsView = "products.view";
    public const string ProductsManage = "products.manage";
    public const string OrdersView = "orders.view";
    public const string OrdersManage = "orders.manage";
    public const string OrdersCancel = "orders.cancel";
    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";
    public const string ChannelsView = "channels.view";
    public const string ChannelsManage = "channels.manage";
    public const string DashboardView = "dashboard.view";
    public const string SettingsView = "settings.view";
    public const string SettingsManage = "settings.manage";
    public const string ExpensesView = "expenses.view";
    public const string ExpensesManage = "expenses.manage";
    public const string ReportsView = "reports.view";
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesView = "roles.view";
    public const string RolesManage = "roles.manage";
    public const string NavigationView = "navigation.view";
    public const string NavigationManage = "navigation.manage";

    public static readonly IReadOnlyList<PermissionDescriptor> Descriptors =
    [
        new(ProductsView, "Xem sản phẩm", "Sản phẩm", "Xem danh sách và chi tiết sản phẩm."),
        new(ProductsManage, "Quản lý sản phẩm", "Sản phẩm", "Tạo và cập nhật sản phẩm."),
        new(OrdersView, "Xem đơn hàng", "Đơn hàng", "Xem danh sách và chi tiết đơn hàng."),
        new(OrdersManage, "Quản lý đơn hàng", "Đơn hàng", "Tạo và cập nhật đơn hàng."),
        new(OrdersCancel, "Hủy đơn hàng", "Đơn hàng", "Hủy đơn hàng đang xử lý."),
        new(CustomersView, "Xem khách hàng", "Khách hàng", "Xem thông tin khách hàng."),
        new(CustomersManage, "Quản lý khách hàng", "Khách hàng", "Cập nhật thông tin khách hàng."),
        new(ChannelsView, "Xem kênh bán", "Cấu hình", "Xem danh sách kênh bán."),
        new(ChannelsManage, "Quản lý kênh bán", "Cấu hình", "Tạo và cập nhật kênh bán."),
        new(DashboardView, "Xem tổng quan", "Báo cáo", "Xem màn hình tổng quan vận hành."),
        new(SettingsView, "Xem cấu hình", "Cấu hình", "Xem dữ liệu cấu hình hệ thống."),
        new(SettingsManage, "Quản lý cấu hình", "Cấu hình", "Thay đổi dữ liệu cấu hình hệ thống."),
        new(ExpensesView, "Xem chi phí", "Tài chính", "Xem danh mục và các khoản chi."),
        new(ExpensesManage, "Quản lý chi phí", "Tài chính", "Tạo, cập nhật và xóa chi phí."),
        new(ReportsView, "Xem báo cáo", "Báo cáo", "Xem và xuất báo cáo tài chính."),
        new(UsersView, "Xem người dùng", "Phân quyền", "Xem tài khoản quản trị."),
        new(UsersManage, "Quản lý người dùng", "Phân quyền", "Tạo, cập nhật và khóa tài khoản."),
        new(RolesView, "Xem vai trò", "Phân quyền", "Xem vai trò và quyền được cấp."),
        new(RolesManage, "Quản lý vai trò", "Phân quyền", "Tạo, cập nhật và xóa vai trò tùy chỉnh."),
        new(NavigationView, "Xem menu và điều hướng", "Phân quyền", "Xem cấu hình menu và route quản trị."),
        new(NavigationManage, "Quản lý menu và điều hướng", "Phân quyền", "Tạo, sắp xếp, bật, tắt và ẩn menu quản trị.")
    ];

    public static readonly IReadOnlyCollection<string> All = Descriptors.Select(item => item.Code).ToArray();

    public static Guid IdFor(string code)
    {
        var index = Descriptors.Select((item, position) => (item, position))
            .Single(pair => pair.item.Code == code).position + 1;
        return Guid.Parse($"10000000-0000-4000-8000-{index:000000000000}");
    }
}

public sealed record PermissionDescriptor(string Code, string Name, string Group, string Description);

public static class BuiltInRolePermissionDefaults
{
    public static IReadOnlyCollection<string> Get(BuiltInRole role) => role switch
    {
        BuiltInRole.Admin => PermissionNames.All,
        BuiltInRole.Manager =>
        [
            PermissionNames.ProductsView,
            PermissionNames.ProductsManage,
            PermissionNames.OrdersView,
            PermissionNames.OrdersManage,
            PermissionNames.OrdersCancel,
            PermissionNames.CustomersView,
            PermissionNames.CustomersManage,
            PermissionNames.ChannelsView,
            PermissionNames.ChannelsManage,
            PermissionNames.DashboardView,
            PermissionNames.SettingsView,
            PermissionNames.SettingsManage,
            PermissionNames.ExpensesView,
            PermissionNames.ExpensesManage,
            PermissionNames.ReportsView
        ],
        BuiltInRole.Staff =>
        [
            PermissionNames.ProductsView,
            PermissionNames.OrdersView,
            PermissionNames.OrdersManage,
            PermissionNames.OrdersCancel,
            PermissionNames.CustomersView,
            PermissionNames.ChannelsView,
            PermissionNames.DashboardView,
            PermissionNames.SettingsView,
            PermissionNames.ExpensesView,
            PermissionNames.ReportsView
        ],
        _ => Array.Empty<string>()
    };
}
