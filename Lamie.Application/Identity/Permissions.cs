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
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public static readonly IReadOnlyCollection<string> All =
    [
        ProductsView,
        ProductsManage,
        OrdersView,
        OrdersManage,
        OrdersCancel,
        CustomersView,
        CustomersManage,
        ChannelsView,
        ChannelsManage,
        DashboardView,
        SettingsView,
        SettingsManage,
        UsersView,
        UsersManage,
        RolesManage
    ];
}

public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<UserRole, IReadOnlyCollection<string>> Mapping =
        new Dictionary<UserRole, IReadOnlyCollection<string>>
        {
            [UserRole.Admin] = PermissionNames.All,
            [UserRole.Manager] =
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
                PermissionNames.SettingsManage
            ],
            [UserRole.Staff] =
            [
                PermissionNames.ProductsView,
                PermissionNames.OrdersView,
                PermissionNames.OrdersManage,
                PermissionNames.OrdersCancel,
                PermissionNames.CustomersView,
                PermissionNames.ChannelsView,
                PermissionNames.DashboardView,
                PermissionNames.SettingsView
            ]
        };

    public static IReadOnlyCollection<string> Get(UserRole role) =>
        Mapping.TryGetValue(role, out var permissions) ? permissions : Array.Empty<string>();
}
