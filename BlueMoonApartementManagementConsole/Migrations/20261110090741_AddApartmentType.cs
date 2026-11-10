using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddApartmentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "apartments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "CanHo");

            migrationBuilder.AddCheckConstraint(
                name: "ck_apartments_type",
                table: "apartments",
                sql: "\"Type\" IN ('CanHo','Kiot','Penthouse')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_apartments_type",
                table: "apartments");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "apartments");
        }
    }
}
