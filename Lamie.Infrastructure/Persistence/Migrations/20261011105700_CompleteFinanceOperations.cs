using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteFinanceOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fin_expense_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expense_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    method = table.Column<int>(type: "int", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_expense_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_fin_expense_payments_fin_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "fin_expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fin_material_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    received_date = table.Column<DateOnly>(type: "date", nullable: false),
                    vendor_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    vendor_address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    vendor_tax_id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    document_status = table.Column<int>(type: "int", nullable: false),
                    tax_eligibility_status = table.Column<int>(type: "int", nullable: false),
                    document_reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    attachment_url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_material_purchases", x => x.id);
                });

            // Preserve payments captured through the transitional single-payment columns.
            // Rows explicitly marked incomplete remain outside cash flow until a user supplies a real paid time.
            migrationBuilder.Sql("""
                INSERT INTO [fin_expense_payments]
                    ([id], [expense_id], [amount], [paid_at], [method], [idempotency_key], [note], [status], [created_at], [created_by_id])
                SELECT NEWID(), [id], [amount], [paid_at], [payment_method],
                    COALESCE([payment_idempotency_key], CONCAT('legacy-expense-', CONVERT(nvarchar(36), [id]))),
                    N'Chuyển từ dữ liệu thanh toán một lần', 1, [updated_at], NULL
                FROM [fin_expenses]
                WHERE [paid_at] IS NOT NULL AND [payment_method] IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "fin_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: false),
                    opening_cash_balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    data_version = table.Column<long>(type: "bigint", nullable: false),
                    stocktake_confirmed_version = table.Column<long>(type: "bigint", nullable: true),
                    shipping_confirmed_version = table.Column<long>(type: "bigint", nullable: true),
                    expenses_confirmed_version = table.Column<long>(type: "bigint", nullable: true),
                    closed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    closed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_periods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fin_material_purchase_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_material_purchase_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_fin_material_purchase_lines_fin_material_purchases_purchase_id",
                        column: x => x.purchase_id,
                        principalTable: "fin_material_purchases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fin_material_purchase_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    method = table.Column<int>(type: "int", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_material_purchase_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_fin_material_purchase_payments_material_purchases_purchase_id",
                        column: x => x.purchase_id,
                        principalTable: "fin_material_purchases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fin_material_stocktake_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group = table.Column<int>(type: "int", nullable: false),
                    opening_value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ending_value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    waste_value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    adjustment_value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    is_estimated = table.Column<bool>(type: "bit", nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    confirmed_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_material_stocktake_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_fin_material_stocktake_lines_fin_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "fin_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fin_period_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    data_version = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fin_period_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_fin_period_events_fin_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "fin_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "auth_permissions",
                columns: new[] { "id", "code", "created_at", "description", "group", "is_active", "is_system", "name", "sort_order", "updated_at" },
                values: new object[] { new Guid("10000000-0000-4000-8000-000000000032"), "finance-periods.reopen", new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Mở lại kỳ đã chốt với lý do và lịch sử kiểm toán.", "Tài chính", true, true, "Mở lại kỳ tài chính", 320, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "auth_role_permissions",
                columns: new[] { "permission_id", "role_id", "granted_at" },
                values: new object[] { new Guid("10000000-0000-4000-8000-000000000032"), new Guid("20000000-0000-4000-8000-000000000001"), new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.Sql("""
                DECLARE @now datetime2 = SYSUTCDATETIME();
                DECLARE @financeGroup uniqueidentifier = (SELECT [id] FROM [auth_navigation] WHERE [key] = N'group.finance');
                IF @financeGroup IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [auth_navigation] WHERE [key] = N'finance.purchases')
                    INSERT INTO [auth_navigation]
                        ([id], [key], [parent_id], [module_key], [page_key], [label], [description], [path], [icon_key],
                         [permission_code], [sort_order], [is_visible], [is_enabled], [is_system], [open_in_new_tab],
                         [created_at], [updated_at], [created_by], [updated_by])
                    VALUES ('90000000-0000-4000-8000-000000000055', N'finance.purchases', @financeGroup, N'reports', N'finance.purchases',
                        N'Mua nguyên liệu', N'Phiếu mua, thanh toán và hồ sơ chứng từ.', N'/admin/finance/purchases', N'package-plus',
                        N'reports.view', 30, 1, 1, 1, 0, @now, @now, NULL, NULL);
                IF @financeGroup IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [auth_navigation] WHERE [key] = N'finance.period-close')
                    INSERT INTO [auth_navigation]
                        ([id], [key], [parent_id], [module_key], [page_key], [label], [description], [path], [icon_key],
                         [permission_code], [sort_order], [is_visible], [is_enabled], [is_system], [open_in_new_tab],
                         [created_at], [updated_at], [created_by], [updated_by])
                    VALUES ('90000000-0000-4000-8000-000000000056', N'finance.period-close', @financeGroup, N'reports', N'finance.period-close',
                        N'Kiểm kê & chốt kỳ', N'Kiểm kê giá vốn, xác nhận và khóa kỳ.', N'/admin/finance/period-close', N'list-checks',
                        N'expenses.manage', 40, 1, 1, 1, 0, @now, @now, NULL, NULL);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_fin_expense_payments_expense_id_paid_at",
                table: "fin_expense_payments",
                columns: new[] { "expense_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fin_expense_payments_idempotency_key",
                table: "fin_expense_payments",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fin_material_purchase_lines_purchase_id_group",
                table: "fin_material_purchase_lines",
                columns: new[] { "purchase_id", "group" });

            migrationBuilder.CreateIndex(
                name: "ix_fin_material_purchase_payments_idempotency_key",
                table: "fin_material_purchase_payments",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fin_material_purchase_payments_purchase_id_paid_at",
                table: "fin_material_purchase_payments",
                columns: new[] { "purchase_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fin_material_purchases_document_status_received_date",
                table: "fin_material_purchases",
                columns: new[] { "document_status", "received_date" });

            migrationBuilder.CreateIndex(
                name: "ix_fin_material_purchases_received_date",
                table: "fin_material_purchases",
                column: "received_date");

            migrationBuilder.CreateIndex(
                name: "ix_fin_material_stocktake_lines_period_id_group",
                table: "fin_material_stocktake_lines",
                columns: new[] { "period_id", "group" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fin_period_events_period_id_occurred_at",
                table: "fin_period_events",
                columns: new[] { "period_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fin_periods_from_to",
                table: "fin_periods",
                columns: new[] { "from", "to" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fin_periods_status_from_to",
                table: "fin_periods",
                columns: new[] { "status", "from", "to" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [auth_navigation] WHERE [key] IN (N'finance.purchases', N'finance.period-close') AND [is_system] = 1;");

            migrationBuilder.DropTable(
                name: "fin_expense_payments");

            migrationBuilder.DropTable(
                name: "fin_material_purchase_lines");

            migrationBuilder.DropTable(
                name: "fin_material_purchase_payments");

            migrationBuilder.DropTable(
                name: "fin_material_stocktake_lines");

            migrationBuilder.DropTable(
                name: "fin_period_events");

            migrationBuilder.DropTable(
                name: "fin_material_purchases");

            migrationBuilder.DropTable(
                name: "fin_periods");

            migrationBuilder.DeleteData(
                table: "auth_role_permissions",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { new Guid("10000000-0000-4000-8000-000000000032"), new Guid("20000000-0000-4000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "auth_permissions",
                keyColumn: "id",
                keyValue: new Guid("10000000-0000-4000-8000-000000000032"));
        }
    }
}
