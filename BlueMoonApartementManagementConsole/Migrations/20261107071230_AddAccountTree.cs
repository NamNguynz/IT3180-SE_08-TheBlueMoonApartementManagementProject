using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GrantedById",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordChangedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_GrantedById",
                table: "users",
                column: "GrantedById");

            migrationBuilder.AddForeignKey(
                name: "FK_users_users_GrantedById",
                table: "users",
                column: "GrantedById",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_users_GrantedById",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_GrantedById",
                table: "users");

            migrationBuilder.DropColumn(
                name: "GrantedById",
                table: "users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "users");
        }
    }
}
