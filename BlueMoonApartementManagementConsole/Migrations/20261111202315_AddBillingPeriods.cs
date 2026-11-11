using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_periods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    ChargeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_periods", x => x.Id);
                    table.CheckConstraint("ck_billing_periods_due_date", "\"DueDate\" >= \"ChargeDate\"");
                    table.CheckConstraint("ck_billing_periods_month", "\"Month\" BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "FK_billing_periods_users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_period_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillingPeriodId = table.Column<int>(type: "integer", nullable: false),
                    FeeItemId = table.Column<int>(type: "integer", nullable: false),
                    FeeRateId = table.Column<int>(type: "integer", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_period_items", x => x.Id);
                    table.CheckConstraint("ck_billing_period_items_rate", "\"Rate\" >= 0");
                    table.ForeignKey(
                        name: "FK_billing_period_items_billing_periods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "billing_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_billing_period_items_fee_items_FeeItemId",
                        column: x => x.FeeItemId,
                        principalTable: "fee_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_period_items_fee_rates_FeeRateId",
                        column: x => x.FeeRateId,
                        principalTable: "fee_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receivables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillingPeriodItemId = table.Column<int>(type: "integer", nullable: false),
                    HouseholdId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receivables", x => x.Id);
                    table.CheckConstraint("ck_receivables_amount", "\"Amount\" >= 0");
                    table.CheckConstraint("ck_receivables_quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_receivables_billing_period_items_BillingPeriodItemId",
                        column: x => x.BillingPeriodItemId,
                        principalTable: "billing_period_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_receivables_households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_period_items_BillingPeriodId_FeeItemId",
                table: "billing_period_items",
                columns: new[] { "BillingPeriodId", "FeeItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_period_items_FeeItemId",
                table: "billing_period_items",
                column: "FeeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_period_items_FeeRateId",
                table: "billing_period_items",
                column: "FeeRateId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_periods_CreatedById",
                table: "billing_periods",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_billing_periods_Year_Month",
                table: "billing_periods",
                columns: new[] { "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_receivables_BillingPeriodItemId_HouseholdId",
                table: "receivables",
                columns: new[] { "BillingPeriodItemId", "HouseholdId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receivables_HouseholdId",
                table: "receivables",
                column: "HouseholdId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receivables");

            migrationBuilder.DropTable(
                name: "billing_period_items");

            migrationBuilder.DropTable(
                name: "billing_periods");
        }
    }
}
