using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddApartmentHouseholdProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apartments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Building = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Floor = table.Column<int>(type: "integer", nullable: false),
                    Area = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    BedroomCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apartments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "households",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApartmentId = table.Column<int>(type: "integer", nullable: false),
                    HouseholdNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    HeadName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HouseNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_households", x => x.Id);
                    table.ForeignKey(
                        name: "FK_households_apartments_ApartmentId",
                        column: x => x.ApartmentId,
                        principalTable: "apartments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "residents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HouseholdId = table.Column<int>(type: "integer", nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Alias = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DateOfBirth = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlaceOfBirth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Hometown = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Ethnicity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Religion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Occupation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Workplace = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IdentityNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IdentityIssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdentityIssuePlace = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    PreviousResidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_residents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_residents_households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_apartments_Code",
                table: "apartments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_households_ApartmentId",
                table: "households",
                column: "ApartmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_households_HouseholdNumber",
                table: "households",
                column: "HouseholdNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_residents_HouseholdId",
                table: "residents",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_residents_IdentityNumber",
                table: "residents",
                column: "IdentityNumber",
                unique: true,
                filter: "\"IdentityNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "residents");

            migrationBuilder.DropTable(
                name: "households");

            migrationBuilder.DropTable(
                name: "apartments");
        }
    }
}
