using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileDeliveredPaidOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @changed_at datetime2 = SYSUTCDATETIME();

                INSERT INTO [sales_order_change_logs]
                    ([id], [order_id], [entity_name], [field_name], [old_value], [new_value],
                     [change_type], [changed_by_id], [changed_by_name], [changed_at], [note])
                SELECT NEWID(), [id], N'Order', N'OrderStatus', N'7', N'4',
                       N'DataReconciled', NULL, N'System migration', @changed_at,
                       N'Đồng bộ đơn đã nhận hàng và đã thanh toán sang trạng thái hoàn tất.'
                FROM [sales_orders]
                WHERE [order_status] = 7 AND [payment_status] = 3;

                UPDATE [sales_orders]
                SET [order_status] = 4,
                    [updated_at] = @changed_at
                WHERE [order_status] = 7 AND [payment_status] = 3;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This is an audited business-data reconciliation. Reverting it automatically could
            // overwrite later operational decisions, so downgrade intentionally preserves the data.
        }
    }
}
