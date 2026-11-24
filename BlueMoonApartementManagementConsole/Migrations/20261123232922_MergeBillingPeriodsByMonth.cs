using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueMoonApartementManagementConsole.Migrations
{
    /// <inheritdoc />
    public partial class MergeBillingPeriodsByMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_billing_periods_Year_Month",
                table: "billing_periods");

            // Gộp các kỳ cũ cùng tháng về một kỳ (Id nhỏ nhất) trước khi áp ràng buộc unique.
            // Khoản thu trùng FeeItemId được gộp vào một dòng, số phải thu của cùng hộ được cộng lại.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    grp RECORD;
                    dup_item RECORD;
                    keep_item integer;
                BEGIN
                    FOR grp IN
                        SELECT "Year", "Month", MIN("Id") AS canonical
                        FROM billing_periods
                        GROUP BY "Year", "Month"
                        HAVING COUNT(*) > 1
                    LOOP
                        FOR dup_item IN
                            SELECT bi."Id", bi."FeeItemId"
                            FROM billing_period_items bi
                            INNER JOIN billing_periods p ON p."Id" = bi."BillingPeriodId"
                            WHERE p."Year" = grp."Year" AND p."Month" = grp."Month"
                              AND bi."BillingPeriodId" <> grp.canonical
                            ORDER BY bi."Id"
                        LOOP
                            SELECT MIN(bi."Id") INTO keep_item
                            FROM billing_period_items bi
                            WHERE bi."BillingPeriodId" = grp.canonical AND bi."FeeItemId" = dup_item."FeeItemId";

                            IF keep_item IS NULL THEN
                                UPDATE billing_period_items SET "BillingPeriodId" = grp.canonical WHERE "Id" = dup_item."Id";
                            ELSE
                                UPDATE receivables AS kept
                                SET "Quantity" = kept."Quantity" + src."Quantity",
                                    "Amount" = kept."Amount" + src."Amount"
                                FROM receivables AS src
                                WHERE src."BillingPeriodItemId" = dup_item."Id"
                                  AND kept."BillingPeriodItemId" = keep_item
                                  AND kept."HouseholdId" = src."HouseholdId";

                                DELETE FROM receivables AS src
                                WHERE src."BillingPeriodItemId" = dup_item."Id"
                                  AND EXISTS (
                                      SELECT 1 FROM receivables AS kept
                                      WHERE kept."BillingPeriodItemId" = keep_item
                                        AND kept."HouseholdId" = src."HouseholdId");

                                UPDATE receivables SET "BillingPeriodItemId" = keep_item
                                WHERE "BillingPeriodItemId" = dup_item."Id";

                                DELETE FROM billing_period_items WHERE "Id" = dup_item."Id";
                            END IF;
                        END LOOP;

                        -- Tiền đã thu và lịch nhắc nộp vẫn thuộc tháng đó, chuyển sang kỳ gốc
                        UPDATE payments SET "BillingPeriodId" = grp.canonical
                        WHERE "BillingPeriodId" IN (
                            SELECT p."Id" FROM billing_periods AS p
                            WHERE p."Year" = grp."Year" AND p."Month" = grp."Month" AND p."Id" <> grp.canonical);

                        UPDATE debt_reminders SET "BillingPeriodId" = grp.canonical
                        WHERE "BillingPeriodId" IN (
                            SELECT p."Id" FROM billing_periods AS p
                            WHERE p."Year" = grp."Year" AND p."Month" = grp."Month" AND p."Id" <> grp.canonical);

                        DELETE FROM billing_periods
                        WHERE "Year" = grp."Year" AND "Month" = grp."Month" AND "Id" <> grp.canonical;
                    END LOOP;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_billing_periods_Year_Month",
                table: "billing_periods",
                columns: new[] { "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Chỉ bỏ ràng buộc; các kỳ đã gộp không thể tách lại như cũ (dữ liệu lịch sử đã sáp nhập).
            migrationBuilder.DropIndex(
                name: "IX_billing_periods_Year_Month",
                table: "billing_periods");

            migrationBuilder.CreateIndex(
                name: "IX_billing_periods_Year_Month",
                table: "billing_periods",
                columns: new[] { "Year", "Month" });
        }
    }
}
