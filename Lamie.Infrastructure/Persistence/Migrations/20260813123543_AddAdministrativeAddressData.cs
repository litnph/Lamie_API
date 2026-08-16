using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lamie.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeAddressData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "delivery_address",
                table: "sales_orders",
                type: "nvarchar(1500)",
                maxLength: 1500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_detail",
                table: "sales_orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "address_scheme",
                table: "sales_orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commune_code",
                table: "sales_orders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commune_name",
                table: "sales_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "district_code",
                table: "sales_orders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "district_name",
                table: "sales_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "full_address_snapshot",
                table: "sales_orders",
                type: "nvarchar(1500)",
                maxLength: 1500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "province_code",
                table: "sales_orders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "province_name",
                table: "sales_orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "geo_administrative_unit_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_unit_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    current_unit_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    transition_type = table.Column<int>(type: "int", nullable: false),
                    source_document = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    dataset_version = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geo_administrative_unit_transitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "geo_administrative_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    scheme = table.Column<int>(type: "int", nullable: false),
                    unit_type = table.Column<int>(type: "int", nullable: false),
                    hierarchy_level = table.Column<int>(type: "int", nullable: false),
                    parent_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    source_document = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    source_reference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    dataset_version = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geo_administrative_units", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_orders_address_scheme_province_code_commune_code",
                table: "sales_orders",
                columns: new[] { "address_scheme", "province_code", "commune_code" });

            migrationBuilder.CreateIndex(
                name: "ix_geo_administrative_unit_transitions_current_unit_code",
                table: "geo_administrative_unit_transitions",
                column: "current_unit_code");

            migrationBuilder.CreateIndex(
                name: "ix_geo_administrative_unit_transitions_legacy_unit_code",
                table: "geo_administrative_unit_transitions",
                column: "legacy_unit_code");

            migrationBuilder.CreateIndex(
                name: "ix_geo_administrative_unit_transitions_legacy_unit_code_current_unit_code",
                table: "geo_administrative_unit_transitions",
                columns: new[] { "legacy_unit_code", "current_unit_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_geo_administrative_unit_transitions_transition_type",
                table: "geo_administrative_unit_transitions",
                column: "transition_type");

            migrationBuilder.CreateIndex(
                name: "ix_geo_administrative_units_scheme_code",
                table: "geo_administrative_units",
                columns: new[] { "scheme", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_geo_administrative_units_scheme_normalized_name",
                table: "geo_administrative_units",
                columns: new[] { "scheme", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "ix_geo_administrative_units_scheme_parent_code_sort_order",
                table: "geo_administrative_units",
                columns: new[] { "scheme", "parent_code", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_geo_administrative_units_scheme_unit_type_is_active",
                table: "geo_administrative_units",
                columns: new[] { "scheme", "unit_type", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "geo_administrative_unit_transitions");
            migrationBuilder.DropTable(name: "geo_administrative_units");

            migrationBuilder.DropIndex(
                name: "ix_sales_orders_address_scheme_province_code_commune_code",
                table: "sales_orders");

            migrationBuilder.DropColumn(name: "address_detail", table: "sales_orders");
            migrationBuilder.DropColumn(name: "address_scheme", table: "sales_orders");
            migrationBuilder.DropColumn(name: "commune_code", table: "sales_orders");
            migrationBuilder.DropColumn(name: "commune_name", table: "sales_orders");
            migrationBuilder.DropColumn(name: "district_code", table: "sales_orders");
            migrationBuilder.DropColumn(name: "district_name", table: "sales_orders");
            migrationBuilder.DropColumn(name: "full_address_snapshot", table: "sales_orders");
            migrationBuilder.DropColumn(name: "province_code", table: "sales_orders");
            migrationBuilder.DropColumn(name: "province_name", table: "sales_orders");

            migrationBuilder.AlterColumn<string>(
                name: "delivery_address",
                table: "sales_orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1500)",
                oldMaxLength: 1500,
                oldNullable: true);
        }
    }
}
