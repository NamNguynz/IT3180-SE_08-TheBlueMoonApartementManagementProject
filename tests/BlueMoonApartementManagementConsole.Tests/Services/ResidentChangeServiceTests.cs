using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test logic ghi nhận biến động nhân khẩu (PBI-007): bảng chuyển trạng thái, kiểm tra ngày,
/// khai báo nhân khẩu mới, nhật ký. Test chấp nhận trên app thật (WBS 4.09.03) do Tester làm riêng.
/// </summary>
[Trait("PBI", "PBI-007")]
public sealed class ResidentChangeServiceTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private readonly FakeProfileRepository _profiles = new();
    private readonly FakeResidentChangeRepository _changes;
    private readonly FakeAuditService _audit = new();
    private readonly ResidentChangeService _service;
    private readonly HouseholdEntity _household;

    public ResidentChangeServiceTests()
    {
        _changes = new FakeResidentChangeRepository(_profiles);
        // FakeTimeProvider mặc định múi giờ UTC, nên "hôm nay" của service là 11/11/2026
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
        _service = new ResidentChangeService(_profiles, _changes, _audit, clock);
        _household = _profiles.SeedHousehold("HK-1", "Nguyen Van An", "Tran Thi Binh");
    }

    private ResidentEntity An => _household.Residents[0];

    // ===== Chuyển đến, tạm trú: khai báo nhân khẩu mới =====

    [Fact]
    public async Task ChuyenDen_AddsPermanentResident_KeepsHistory_AndAuditsWithoutName()
    {
        var result = await RecordAsync(new RecordResidentChangeRequest
        {
            Type = ResidentChangeType.ChuyenDen,
            NewResident = NewResident("Le Van Cuong"),
            FromDate = Today,
            Place = "  Số 1 Đại Cồ Việt, Hà Nội  "
        });

        Assert.Equal(ProfileStatus.Success, result.Status);
        var added = Assert.Single(_household.Residents, r => r.FullName == "Le Van Cuong");
        Assert.Equal(ResidenceStatus.ThuongTru, added.ResidenceStatus);
        // Nơi ở trước nhập một lần ở ô nơi đi/đến, được chép vào hồ sơ nhân khẩu
        Assert.Equal("Số 1 Đại Cồ Việt, Hà Nội", added.PreviousResidence);

        var change = Assert.Single(_changes.Saved);
        Assert.Equal(added.Id, change.ResidentId);
        Assert.Equal(ResidentChangeType.ChuyenDen, change.Type);

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.RecordResidentChange, log.Action);
        Assert.Equal(added.Id.ToString(), log.Target);
        Assert.False(_audit.AnyEntryContains("Le Van Cuong"));
        Assert.Contains("HK-1", log.Detail);

        Assert.Equal(3, result.Data!.Residents.Count);
        Assert.Equal(Today, Assert.Single(result.Data.Changes).FromDate);
    }

    [Fact]
    public async Task TamTru_NeedsEndDate_ThenAddsTemporaryResident_WithStatusUntil()
    {
        var withoutEnd = await RecordAsync(new RecordResidentChangeRequest
        {
            Type = ResidentChangeType.TamTru,
            NewResident = NewResident("Pham Thi Dung"),
            FromDate = Today
        });
        Assert.Equal(ProfileStatus.Invalid, withoutEnd.Status);
        Assert.Equal("Tạm trú, tạm vắng phải có ngày kết thúc dự kiến.", withoutEnd.Message);
        Assert.Equal(2, _household.Residents.Count);

        var result = await RecordAsync(new RecordResidentChangeRequest
        {
            Type = ResidentChangeType.TamTru,
            NewResident = NewResident("Pham Thi Dung"),
            FromDate = Today,
            ToDate = Today.AddMonths(6)
        });

        Assert.Equal(ProfileStatus.Success, result.Status);
        var resident = Assert.Single(result.Data!.Residents, r => r.FullName == "Pham Thi Dung");
        Assert.Equal(ResidenceStatus.TamTru, resident.ResidenceStatus);
        Assert.Equal(Today.AddMonths(6), resident.StatusUntil);
    }

    [Fact]
    public async Task NewResident_WithIdentityAlreadyUsed_IsRejected()
    {
        An.IdentityNumber = "001234567890";
        var resident = NewResident("Le Van Cuong") with { IdentityNumber = " 001234567890 " };

        var result = await RecordAsync(new RecordResidentChangeRequest
        {
            Type = ResidentChangeType.ChuyenDen,
            NewResident = resident,
            FromDate = Today
        });

        Assert.Equal(ProfileStatus.Invalid, result.Status);
        Assert.Equal("Số CMND/CCCD đã được sử dụng.", result.Message);
        Assert.Empty(_changes.Saved);
    }

    // ===== Tạm vắng, trở về, chuyển đi: nhân khẩu có sẵn =====

    [Fact]
    public async Task TamVang_ThenTroVe_ReturnsToPermanent_AndHistoryIsNewestFirst()
    {
        var away = await RecordAsync(TamVang(An.Id, Today.AddDays(-10), Today.AddMonths(3)));
        Assert.Equal(ProfileStatus.Success, away.Status);
        Assert.Equal(ResidenceStatus.TamVang, An.ResidenceStatus);
        Assert.Equal(Today.AddMonths(3), away.Data!.Residents.Single(r => r.Id == An.Id).StatusUntil);

        var again = await RecordAsync(TamVang(An.Id, Today, Today.AddMonths(1)));
        Assert.Equal(ProfileStatus.Invalid, again.Status);
        Assert.Equal("Nhân khẩu đang tạm vắng.", again.Message);

        var back = await RecordAsync(new RecordResidentChangeRequest
        {
            Type = ResidentChangeType.TroVe,
            ResidentId = An.Id,
            FromDate = Today
        });

        Assert.Equal(ProfileStatus.Success, back.Status);
        Assert.Equal(ResidenceStatus.ThuongTru, An.ResidenceStatus);
        var view = back.Data!.Residents.Single(r => r.Id == An.Id);
        Assert.Null(view.StatusUntil);
        Assert.Equal(Today, view.LastChangeDate);
        Assert.Equal(new[] { ResidentChangeType.TroVe, ResidentChangeType.TamVang }, back.Data.Changes.Select(c => c.Type));
        Assert.Equal(2, _audit.Entries.Count);
    }

    [Fact]
    public async Task ChuyenDi_NeedsDestination_KeepsResidentRecord_AsMovedOut()
    {
        var withoutPlace = await RecordAsync(new RecordResidentChangeRequest
        {
            Type = ResidentChangeType.ChuyenDi,
            ResidentId = An.Id,
            FromDate = Today
        });
        Assert.Equal(ProfileStatus.Invalid, withoutPlace.Status);
        Assert.Equal("Hãy nhập nơi chuyển đến.", withoutPlace.Message);

        var result = await RecordAsync(new RecordResidentChangeRequest
        {
            Type = ResidentChangeType.ChuyenDi,
            ResidentId = An.Id,
            FromDate = Today,
            Place = "Đà Nẵng",
            Reason = "Chuyển công tác"
        });

        Assert.Equal(ProfileStatus.Success, result.Status);
        Assert.Equal(ResidenceStatus.DaChuyenDi, An.ResidenceStatus);
        Assert.Equal(2, _household.Residents.Count); // hồ sơ vẫn giữ, không xóa
        Assert.Equal(An.Id, result.Data!.Residents[^1].Id); // người đã chuyển đi xếp cuối
    }

    [Theory]
    [InlineData(ResidenceStatus.ThuongTru, ResidentChangeType.TroVe, "Chỉ nhân khẩu đang tạm vắng mới ghi nhận trở về.")]
    [InlineData(ResidenceStatus.TamTru, ResidentChangeType.TamVang, "Chỉ nhân khẩu thường trú mới khai báo tạm vắng.")]
    [InlineData(ResidenceStatus.DaChuyenDi, ResidentChangeType.ChuyenDi, "Nhân khẩu đã chuyển đi, không ghi nhận thêm biến động.")]
    [InlineData(ResidenceStatus.DaChuyenDi, ResidentChangeType.TamVang, "Nhân khẩu đã chuyển đi, không ghi nhận thêm biến động.")]
    public async Task TransitionNotInTable_IsRejected_AndNothingChanges(
        ResidenceStatus current, ResidentChangeType type, string message)
    {
        An.ResidenceStatus = current;
        var request = new RecordResidentChangeRequest
        {
            Type = type,
            ResidentId = An.Id,
            FromDate = Today,
            ToDate = type == ResidentChangeType.TamVang ? Today.AddDays(30) : null,
            Place = "Hải Phòng"
        };

        var result = await RecordAsync(request);

        Assert.Equal(ProfileStatus.Invalid, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Equal(current, An.ResidenceStatus);
        Assert.Empty(_changes.Saved);
        Assert.Empty(_audit.Entries);
    }

    // ===== Kiểm tra ngày và dữ liệu đầu vào (AC-002) =====

    public static TheoryData<RecordResidentChangeRequest, string> InvalidRequests => new()
    {
        { new() { Type = ResidentChangeType.TroVe, ResidentId = 1 }, "Ngày biến động là bắt buộc." },
        { new() { Type = ResidentChangeType.ChuyenDi, ResidentId = 1, FromDate = new DateOnly(2026, 11, 12), Place = "Huế" },
            "Ngày biến động không được sau ngày hiện tại." },
        { new() { Type = ResidentChangeType.ChuyenDi, ResidentId = 1, FromDate = new DateOnly(2026, 11, 1), ToDate = new DateOnly(2026, 12, 1), Place = "Huế" },
            "Chỉ tạm trú, tạm vắng mới có ngày kết thúc." },
        { new() { Type = ResidentChangeType.TamVang, ResidentId = 1, FromDate = new DateOnly(2026, 11, 1), ToDate = new DateOnly(2026, 11, 1), Place = "Huế" },
            "Ngày kết thúc phải sau ngày bắt đầu." },
        { new() { Type = ResidentChangeType.TamVang, ResidentId = 1, FromDate = new DateOnly(2026, 11, 1), ToDate = new DateOnly(2026, 12, 1), Place = "   " },
            "Hãy nhập nơi đến trong thời gian tạm vắng." },
        { new() { Type = ResidentChangeType.TamVang, FromDate = new DateOnly(2026, 11, 1), ToDate = new DateOnly(2026, 12, 1), Place = "Huế" },
            "Hãy chọn nhân khẩu cần ghi nhận biến động." },
        { new() { Type = ResidentChangeType.TroVe, ResidentId = 99, FromDate = new DateOnly(2026, 11, 1) },
            "Nhân khẩu không thuộc hộ gia đình này." },
        { new() { Type = ResidentChangeType.ChuyenDen, FromDate = new DateOnly(2026, 11, 1) },
            "Hãy nhập thông tin nhân khẩu mới." },
        { new() { Type = ResidentChangeType.ChuyenDen, ResidentId = 1, FromDate = new DateOnly(2026, 11, 1) },
            "Chuyển đến, tạm trú là khai báo nhân khẩu mới, không chọn nhân khẩu có sẵn." },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequest_IsRejected_WithMessagePointingToTheField(RecordResidentChangeRequest request, string message)
    {
        var result = await RecordAsync(request);

        Assert.Equal(ProfileStatus.Invalid, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Empty(_changes.Saved);
    }

    [Fact]
    public async Task FromDate_BeforePreviousChangeOfResident_IsRejected()
    {
        await RecordAsync(TamVang(An.Id, Today.AddDays(-5), Today.AddMonths(1)));

        var result = await RecordAsync(new RecordResidentChangeRequest
        {
            Type = ResidentChangeType.TroVe,
            ResidentId = An.Id,
            FromDate = Today.AddDays(-10)
        });

        Assert.Equal(ProfileStatus.Invalid, result.Status);
        Assert.Equal("Ngày biến động không được trước lần biến động gần nhất (06/11/2026).", result.Message);
        Assert.Equal(ResidenceStatus.TamVang, An.ResidenceStatus);
    }

    [Fact]
    public async Task UnknownHousehold_IsNotFound()
    {
        var result = await _service.RecordAsync(1, "admin", 404, TamVang(An.Id, Today, Today.AddDays(7)));

        Assert.Equal(ProfileStatus.NotFound, result.Status);
        Assert.Null(await _service.GetHouseholdResidencyAsync(404));
    }

    // ===== Nhãn tiếng Việt =====

    [Fact]
    public void Labels_CoverEveryChangeTypeAndResidenceStatus_Once()
    {
        Assert.Equal(Enum.GetValues<ResidentChangeType>().Order(), ResidentChangeLabels.Types.Select(x => x.Type).Order());
        Assert.Equal(Enum.GetValues<ResidenceStatus>().Order(), ResidentChangeLabels.Statuses.Select(x => x.Status).Order());
        Assert.All(ResidentChangeLabels.Types, x => Assert.False(string.IsNullOrWhiteSpace(x.Label)));
        Assert.All(ResidentChangeLabels.Statuses, x => Assert.False(string.IsNullOrWhiteSpace(x.Label)));
    }

    // ===== Helpers =====

    private Task<ProfileOperationResult<HouseholdResidencyResponse>> RecordAsync(RecordResidentChangeRequest request) =>
        _service.RecordAsync(1, "admin", _household.Id, request);

    private static RecordResidentChangeRequest TamVang(int residentId, DateOnly from, DateOnly to) => new()
    {
        Type = ResidentChangeType.TamVang,
        ResidentId = residentId,
        FromDate = from,
        ToDate = to,
        Place = "Quê nội, Nam Định"
    };

    private static ResidentProfileRequest NewResident(string name) => new()
    {
        FullName = name,
        DateOfBirth = new DateTimeOffset(1995, 3, 2, 0, 0, 0, TimeSpan.FromHours(7)),
        Gender = "Nam"
    };

    /// <summary>Chỉ cài hai hàm ResidentChangeService dùng; hàm khác ném lỗi để lộ ngay nếu service gọi nhầm.</summary>
    private sealed class FakeProfileRepository : IApartmentProfileRepository
    {
        private readonly List<HouseholdEntity> _households = [];
        private int _nextResidentId = 1;

        public HouseholdEntity SeedHousehold(string number, params string[] residentNames)
        {
            var household = new HouseholdEntity
            {
                Id = _households.Count + 1,
                ApartmentId = 1,
                HouseholdNumber = number,
                HeadName = residentNames[0]
            };
            foreach (var name in residentNames)
            {
                household.Residents.Add(new ResidentEntity
                {
                    Id = _nextResidentId++,
                    HouseholdId = household.Id,
                    FullName = name,
                    Gender = "Nam",
                    DateOfBirth = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero)
                });
            }
            _households.Add(household);
            return household;
        }

        /// <summary>Giống EF khi lưu: nhân khẩu mới (Id = 0) được cấp Id.</summary>
        public void AssignNewResidentIds()
        {
            foreach (var resident in _households.SelectMany(h => h.Residents).Where(r => r.Id == 0))
                resident.Id = _nextResidentId++;
        }

        public Task<HouseholdEntity?> GetHouseholdByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(_households.SingleOrDefault(h => h.Id == id));

        public Task<bool> ResidentIdentityExistsAsync(string identityNumber, int? exceptId = null, CancellationToken ct = default) =>
            Task.FromResult(_households.SelectMany(h => h.Residents)
                .Any(r => r.IdentityNumber == identityNumber && r.Id != exceptId));

        public Task<List<ApartmentEntity>> GetApartmentsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ApartmentEntity?> GetApartmentByIdAsync(int id, bool tracking, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<HouseholdEntity?> GetHouseholdByApartmentIdAsync(int apartmentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ApartmentCodeExistsAsync(string code, int? exceptId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HouseholdNumberExistsAsync(string number, int? exceptId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddApartmentAsync(ApartmentEntity apartment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddHouseholdAsync(HouseholdEntity household, CancellationToken ct = default) => throw new NotSupportedException();

        // Service lưu qua IResidentChangeRepository (cùng AppDbContext trong app thật)
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeResidentChangeRepository(FakeProfileRepository profiles) : IResidentChangeRepository
    {
        private readonly List<ResidentChangeEntity> _pending = [];

        public List<ResidentChangeEntity> Saved { get; } = [];

        public Task<List<ResidentChangeEntity>> GetByHouseholdAsync(int householdId, CancellationToken ct = default) =>
            Task.FromResult(Saved.Where(c => c.HouseholdId == householdId)
                .OrderByDescending(c => c.FromDate)
                .ThenByDescending(c => c.Id)
                .ToList());

        public Task AddAsync(ResidentChangeEntity change, CancellationToken ct = default)
        {
            _pending.Add(change);
            return Task.CompletedTask;
        }

        /// <summary>Giống EF: cấp Id cho nhân khẩu mới rồi điền ResidentId của biến động theo navigation.</summary>
        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            profiles.AssignNewResidentIds();
            foreach (var change in _pending)
            {
                change.Id = Saved.Count + 1;
                change.ResidentId = change.Resident.Id;
                change.RecordedBy = new AppUserEntity { Id = change.RecordedById, Username = "admin" };
                Saved.Add(change);
            }
            _pending.Clear();
            return Task.CompletedTask;
        }
    }
}
