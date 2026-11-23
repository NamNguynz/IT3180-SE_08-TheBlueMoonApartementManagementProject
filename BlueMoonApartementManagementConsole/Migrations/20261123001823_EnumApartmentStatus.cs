using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class EnumApartmentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dữ liệu cũ lưu tiếng Việt tự do; đổi sang tên enum trước khi thêm check constraint.
            // 'Đang sử dụng'/'Đang ở' → DaChoThue, 'Trống' và giá trị lạ → DangChoThue.
            migrationBuilder.Sql(@"
                UPDATE apartments
                SET ""Status"" = CASE
                    WHEN ""Status"" IN ('Đã cho thuê', 'DaChoThue', 'Đang sử dụng', 'Đang ở') THEN 'DaChoThue'
                    WHEN ""Status"" IN ('Đang sửa chữa', 'DangSuaChua') THEN 'DangSuaChua'
                    ELSE 'DangChoThue'
                END;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "apartments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "DangChoThue",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddCheckConstraint(
                name: "ck_apartments_status",
                table: "apartments",
                sql: "\"Status\" IN ('DangChoThue','DaChoThue','DangSuaChua')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_apartments_status",
                table: "apartments");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "apartments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "DangChoThue");

            migrationBuilder.Sql(@"
                UPDATE apartments
                SET ""Status"" = CASE ""Status""
                    WHEN 'DangChoThue' THEN 'Đang cho thuê'
                    WHEN 'DaChoThue' THEN 'Đã cho thuê'
                    WHEN 'DangSuaChua' THEN 'Đang sửa chữa'
                END;
            ");
        }
    }
}
