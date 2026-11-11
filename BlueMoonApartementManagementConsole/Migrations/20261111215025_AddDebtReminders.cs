using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "debt_reminders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillingPeriodId = table.Column<int>(type: "integer", nullable: false),
                    HouseholdId = table.Column<int>(type: "integer", nullable: false),
                    Remaining = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RemindedById = table.Column<int>(type: "integer", nullable: false),
                    RemindedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debt_reminders", x => x.Id);
                    table.CheckConstraint("ck_debt_reminders_channel", "\"Channel\" IN ('GiayBao','DienThoai','TrucTiep','TinNhan')");
                    table.CheckConstraint("ck_debt_reminders_remaining", "\"Remaining\" > 0");
                    table.ForeignKey(
                        name: "FK_debt_reminders_billing_periods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "billing_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_debt_reminders_households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_debt_reminders_users_RemindedById",
                        column: x => x.RemindedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_debt_reminders_BillingPeriodId_HouseholdId",
                table: "debt_reminders",
                columns: new[] { "BillingPeriodId", "HouseholdId" });

            migrationBuilder.CreateIndex(
                name: "IX_debt_reminders_HouseholdId",
                table: "debt_reminders",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_debt_reminders_RemindedById",
                table: "debt_reminders",
                column: "RemindedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "debt_reminders");
        }
    }
}
