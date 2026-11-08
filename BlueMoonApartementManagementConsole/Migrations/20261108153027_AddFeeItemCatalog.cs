using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeItemCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fee_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FeeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FeeScope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FeeCycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CalculationBasis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_items", x => x.Id);
                    table.CheckConstraint("ck_fee_items_calculation_basis", "\"CalculationBasis\" IN ('DienTich','NhanKhau','Ho','CoDinh')");
                    table.CheckConstraint("ck_fee_items_fee_cycle", "\"FeeCycle\" IN ('MotLan','Thang','Nam')");
                    table.CheckConstraint("ck_fee_items_fee_scope", "\"FeeScope\" IN ('Chung','ChiDinh')");
                    table.CheckConstraint("ck_fee_items_fee_type", "\"FeeType\" IN ('BatBuoc','TuNguyen','BaoTriRieng')");
                    table.ForeignKey(
                        name: "FK_fee_items_users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_rates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FeeItemId = table.Column<int>(type: "integer", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_rates", x => x.Id);
                    table.CheckConstraint("ck_fee_rates_period", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\"");
                    table.CheckConstraint("ck_fee_rates_rate", "\"Rate\" >= 0");
                    table.ForeignKey(
                        name: "FK_fee_rates_fee_items_FeeItemId",
                        column: x => x.FeeItemId,
                        principalTable: "fee_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fee_items_Code",
                table: "fee_items",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_items_CreatedById",
                table: "fee_items",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_fee_rates_FeeItemId_EffectiveFrom",
                table: "fee_rates",
                columns: new[] { "FeeItemId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fee_rates");

            migrationBuilder.DropTable(
                name: "fee_items");
        }
    }
}
