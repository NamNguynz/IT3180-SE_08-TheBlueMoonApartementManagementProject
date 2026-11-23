using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Security;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

public interface IResidentListRepository
{
    Task<PagedResult<ResidentListRow>> QueryAsync(ResidentListQuery query, CancellationToken ct = default);
}

/// <summary>
/// Truy vấn trang Hộ &amp; nhân khẩu. Các cột lưu rõ được lọc trên SQL; Dân tộc/Tôn giáo được mã hóa
/// (PBI-030) nên lọc sau khi giải mã, còn CCCD chỉ khớp đúng số qua cột hash.
/// </summary>
public sealed class ResidentListRepository(AppDbContext db, IPersonalDataEncryptor personalData)
    : IResidentListRepository
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<PagedResult<ResidentListRow>> QueryAsync(
        ResidentListQuery query, CancellationToken ct = default)
    {
        var filtered = BuildFilteredQuery(query);
        var encryptedFilter = !string.IsNullOrWhiteSpace(query.Ethnicity) ||
                              !string.IsNullOrWhiteSpace(query.Religion);

        // Không có lọc trên cột mã hóa: đếm và lấy đúng một trang trên SQL.
        if (!encryptedFilter)
        {
            var total = await filtered.CountAsync(ct);
            var items = await filtered
                .OrderBy(r => r.FullName)
                .ThenBy(r => r.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(r => new ResidentListRow(
                    r.Id, r.FullName, r.DateOfBirth, r.Gender, r.PlaceOfBirth, r.Hometown,
                    r.Ethnicity, r.Religion, r.Workplace, r.IdentityNumber))
                .ToListAsync(ct);
            return new PagedResult<ResidentListRow>(items, total, query.Page, query.PageSize);
        }

        // Có lọc Dân tộc/Tôn giáo: EF giải mã khi materialize, lọc tiếp trong bộ nhớ rồi mới phân trang.
        var candidates = await filtered.OrderBy(r => r.FullName).ThenBy(r => r.Id).ToListAsync(ct);
        var ethnicity = query.Ethnicity?.Trim();
        var religion = query.Religion?.Trim();

        var matched = candidates.Where(r =>
            (string.IsNullOrWhiteSpace(ethnicity) ||
             Contains(r.Ethnicity, ethnicity)) &&
            (string.IsNullOrWhiteSpace(religion) ||
             Contains(r.Religion, religion))).ToList();

        var itemsAfterDecrypt = matched
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToRow)
            .ToList();

        return new PagedResult<ResidentListRow>(itemsAfterDecrypt, matched.Count, query.Page, query.PageSize);
    }

    private IQueryable<ResidentEntity> BuildFilteredQuery(ResidentListQuery query)
    {
        var filtered = db.Residents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.FullName))
            filtered = filtered.Where(r => EF.Functions.ILike(r.FullName, $"%{query.FullName.Trim()}%"));
        if (query.DateOfBirthFrom is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), VietnamOffset).ToUniversalTime();
            filtered = filtered.Where(r => r.DateOfBirth >= start);
        }
        if (query.DateOfBirthTo is { } to)
        {
            // Bao gồm cả ngày "đến ngày": lấy đầu ngày kế tiếp làm mốc loại trừ.
            // Npgsql chỉ nhận DateTimeOffset offset 0 cho cột timestamp with time zone.
            var exclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), VietnamOffset)
                .ToUniversalTime();
            filtered = filtered.Where(r => r.DateOfBirth < exclusive);
        }
        if (!string.IsNullOrWhiteSpace(query.Gender))
            filtered = filtered.Where(r => r.Gender == query.Gender.Trim());
        if (!string.IsNullOrWhiteSpace(query.PlaceOfBirth))
            filtered = filtered.Where(r => r.PlaceOfBirth != null &&
                                           EF.Functions.ILike(r.PlaceOfBirth, $"%{query.PlaceOfBirth.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(query.Hometown))
            filtered = filtered.Where(r => r.Hometown != null &&
                                           EF.Functions.ILike(r.Hometown, $"%{query.Hometown.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(query.Workplace))
            filtered = filtered.Where(r => r.Workplace != null &&
                                           EF.Functions.ILike(r.Workplace, $"%{query.Workplace.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(query.IdentityNumber))
        {
            // Bản mã ngẫu nhiên nên không LIKE được; hash là cách duy nhất để tra đúng số CCCD.
            var hash = personalData.HashIdentity(query.IdentityNumber);
            filtered = filtered.Where(r => r.IdentityNumberHash == hash);
        }

        return filtered;
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static ResidentListRow ToRow(ResidentEntity r) => new(
        r.Id, r.FullName, r.DateOfBirth, r.Gender, r.PlaceOfBirth, r.Hometown,
        r.Ethnicity, r.Religion, r.Workplace, r.IdentityNumber);
}
