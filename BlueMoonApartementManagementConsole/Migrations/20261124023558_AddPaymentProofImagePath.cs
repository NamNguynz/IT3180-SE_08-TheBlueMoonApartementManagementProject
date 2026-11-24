using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentProofImagePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProofImagePath",
                table: "payments",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProofImagePath",
                table: "payments");
        }
    }
}
