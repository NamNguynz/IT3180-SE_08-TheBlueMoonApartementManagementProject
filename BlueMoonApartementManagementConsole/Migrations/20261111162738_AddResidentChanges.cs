using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResidenceStatus",
                table: "residents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ThuongTru");

            migrationBuilder.CreateTable(
                name: "resident_changes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HouseholdId = table.Column<int>(type: "integer", nullable: false),
                    ResidentId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Place = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedById = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resident_changes", x => x.Id);
                    table.CheckConstraint("ck_resident_changes_period", "\"ToDate\" IS NULL OR \"ToDate\" > \"FromDate\"");
                    table.CheckConstraint("ck_resident_changes_type", "\"Type\" IN ('ChuyenDen','TamTru','TamVang','TroVe','ChuyenDi')");
                    table.ForeignKey(
                        name: "FK_resident_changes_households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_resident_changes_residents_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "residents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_resident_changes_users_RecordedById",
                        column: x => x.RecordedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_residents_residence_status",
                table: "residents",
                sql: "\"ResidenceStatus\" IN ('ThuongTru','TamTru','TamVang','DaChuyenDi')");

            migrationBuilder.CreateIndex(
                name: "IX_resident_changes_HouseholdId_FromDate",
                table: "resident_changes",
                columns: new[] { "HouseholdId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_resident_changes_RecordedById",
                table: "resident_changes",
                column: "RecordedById");

            migrationBuilder.CreateIndex(
                name: "IX_resident_changes_ResidentId",
                table: "resident_changes",
                column: "ResidentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resident_changes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_residents_residence_status",
                table: "residents");

            migrationBuilder.DropColumn(
                name: "ResidenceStatus",
                table: "residents");
        }
    }
}
