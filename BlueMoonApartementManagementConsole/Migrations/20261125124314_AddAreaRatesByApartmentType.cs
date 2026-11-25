using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaRatesByApartmentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Rate",
                table: "receivables",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Kỳ thu cũ đã chụp đơn giá ở billing_period_items; sao chép sang từng dòng phải thu để
            // bảng kê cũ vẫn giải thích đúng "Amount = Rate × Quantity" (giá theo loại chỉ áp dụng cho kỳ mới).
            migrationBuilder.Sql(@"
                UPDATE ""receivables"" r
                SET ""Rate"" = i.""Rate""
                FROM ""billing_period_items"" i
                WHERE i.""Id"" = r.""BillingPeriodItemId"";");

            migrationBuilder.AddColumn<string>(
                name: "RateMode",
                table: "fee_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Chung");

            migrationBuilder.AlterColumn<decimal>(
                name: "Rate",
                table: "billing_period_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "FeeRateId",
                table: "billing_period_items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "RateMode",
                table: "billing_period_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Chung");

            migrationBuilder.CreateTable(
                name: "fee_type_rates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FeeItemId = table.Column<int>(type: "integer", nullable: false),
                    ApartmentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_type_rates", x => x.Id);
                    table.CheckConstraint("ck_fee_type_rates_apartment_type", "\"ApartmentType\" IN ('CanHo','Kiot','Penthouse')");
                    table.CheckConstraint("ck_fee_type_rates_period", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\"");
                    table.CheckConstraint("ck_fee_type_rates_rate", "\"Rate\" >= 0");
                    table.ForeignKey(
                        name: "FK_fee_type_rates_fee_items_FeeItemId",
                        column: x => x.FeeItemId,
                        principalTable: "fee_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_period_item_type_rates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillingPeriodItemId = table.Column<int>(type: "integer", nullable: false),
                    ApartmentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FeeTypeRateId = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_period_item_type_rates", x => x.Id);
                    table.CheckConstraint("ck_bp_item_type_rates_apartment_type", "\"ApartmentType\" IN ('CanHo','Kiot','Penthouse')");
                    table.CheckConstraint("ck_bp_item_type_rates_rate", "\"Rate\" >= 0");
                    table.ForeignKey(
                        name: "FK_billing_period_item_type_rates_billing_period_items_Billing~",
                        column: x => x.BillingPeriodItemId,
                        principalTable: "billing_period_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_billing_period_item_type_rates_fee_type_rates_FeeTypeRateId",
                        column: x => x.FeeTypeRateId,
                        principalTable: "fee_type_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_receivables_rate",
                table: "receivables",
                sql: "\"Rate\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fee_items_rate_mode",
                table: "fee_items",
                sql: "\"RateMode\" IN ('Chung','TheoLoai')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_billing_period_items_rate_mode",
                table: "billing_period_items",
                sql: "\"RateMode\" IN ('Chung','TheoLoai')");

            migrationBuilder.CreateIndex(
                name: "IX_billing_period_item_type_rates_BillingPeriodItemId_Apartmen~",
                table: "billing_period_item_type_rates",
                columns: new[] { "BillingPeriodItemId", "ApartmentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_period_item_type_rates_FeeTypeRateId",
                table: "billing_period_item_type_rates",
                column: "FeeTypeRateId");

            migrationBuilder.CreateIndex(
                name: "IX_fee_type_rates_FeeItemId_ApartmentType_EffectiveFrom",
                table: "fee_type_rates",
                columns: new[] { "FeeItemId", "ApartmentType", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_period_item_type_rates");

            migrationBuilder.DropTable(
                name: "fee_type_rates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receivables_rate",
                table: "receivables");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fee_items_rate_mode",
                table: "fee_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_billing_period_items_rate_mode",
                table: "billing_period_items");

            migrationBuilder.DropColumn(
                name: "Rate",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "RateMode",
                table: "fee_items");

            migrationBuilder.DropColumn(
                name: "RateMode",
                table: "billing_period_items");

            migrationBuilder.AlterColumn<decimal>(
                name: "Rate",
                table: "billing_period_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "FeeRateId",
                table: "billing_period_items",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
