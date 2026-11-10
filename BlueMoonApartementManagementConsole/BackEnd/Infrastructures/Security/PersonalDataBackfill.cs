using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Security;

/// <summary>
/// Mã hóa các dòng nhân khẩu còn lưu bản rõ (tạo trước khi có PBI-030), và tính hash số giấy tờ còn thiếu.
/// Program.cs gọi ngay sau khi chạy migration. Chạy nhiều lần không sao: lần sau không còn dòng nào cần xử lý.
/// </summary>
public static class PersonalDataBackfill
{
    public static async Task<int> EncryptLegacyRowsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var encryptedPattern = PersonalDataEncryptor.Prefix + "%";

        var ids = await db.Database.SqlQuery<int>($"""
            SELECT "Id" AS "Value" FROM residents
            WHERE ("IdentityNumber" IS NOT NULL AND "IdentityNumber" NOT LIKE {encryptedPattern})
               OR ("IdentityIssuePlace" IS NOT NULL AND "IdentityIssuePlace" NOT LIKE {encryptedPattern})
               OR ("Ethnicity" IS NOT NULL AND "Ethnicity" NOT LIKE {encryptedPattern})
               OR ("Religion" IS NOT NULL AND "Religion" NOT LIKE {encryptedPattern})
               OR ("PreviousResidence" IS NOT NULL AND "PreviousResidence" NOT LIKE {encryptedPattern})
               OR ("IdentityNumber" IS NOT NULL AND "IdentityNumberHash" IS NULL)
            """).ToListAsync(ct);

        if (ids.Count == 0)
            return 0;

        // Đọc qua EF: bản rõ cũ được trả nguyên (Unprotect). Đánh dấu đã sửa để EF ghi lại qua converter,
        // và AppDbContext.SaveChanges tự tính IdentityNumberHash.
        var residents = await db.Residents.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        foreach (var resident in residents)
        {
            var entry = db.Entry(resident);
            entry.Property(r => r.IdentityNumber).IsModified = true;
            entry.Property(r => r.IdentityIssuePlace).IsModified = true;
            entry.Property(r => r.Ethnicity).IsModified = true;
            entry.Property(r => r.Religion).IsModified = true;
            entry.Property(r => r.PreviousResidence).IsModified = true;
        }

        await db.SaveChangesAsync(ct);
        return residents.Count;
    }
}
