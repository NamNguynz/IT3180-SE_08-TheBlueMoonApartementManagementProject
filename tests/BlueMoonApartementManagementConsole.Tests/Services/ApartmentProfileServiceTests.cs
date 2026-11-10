using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;

namespace BlueMoonApartementManagementConsole.Tests.Services;

public sealed class ApartmentProfileServiceTests
{
    private readonly FakeApartmentProfileRepository _repository = new();
    private readonly FakeAuditService _audit = new();
    private readonly ApartmentProfileService _service;

    public ApartmentProfileServiceTests() =>
        _service = new ApartmentProfileService(_repository, _audit);

    [Fact]
    public async Task CreateApartment_NormalizesUniqueCode_AndWritesAudit()
    {
        var result = await _service.CreateApartmentAsync(1, "admin", ApartmentRequest(" a-101 "));

        Assert.Equal(ProfileStatus.Success, result.Status);
        Assert.Equal("A-101", result.Data!.Code);
        Assert.Equal(AuditAction.CreateApartmentProfile, Assert.Single(_audit.Entries).Action);

        var duplicate = await _service.CreateApartmentAsync(1, "admin", ApartmentRequest("a-101"));
        Assert.Equal(ProfileStatus.Conflict, duplicate.Status);
    }

    [Fact]
    public async Task CreateHousehold_NormalizesCalendarDates_AndIncludesResidents()
    {
        var apartment = _repository.SeedApartment("A-101");
        var registeredAt = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(7));
        var birthDate = new DateTimeOffset(1980, 4, 20, 0, 0, 0, TimeSpan.FromHours(7));

        var result = await _service.CreateHouseholdAsync(
            1, "admin", apartment.Id, new CreateHouseholdRequest
            {
                HouseholdNumber = " HK-1 ",
                HeadName = " Nguyen Van An ",
                HouseNumber = " A-101 ",
                RegisteredAt = registeredAt,
                Residents = [Resident(" Nguyen Van An ", birthDate)]
            });

        Assert.Equal(ProfileStatus.Success, result.Status);
        Assert.Equal("HK-1", result.Data!.HouseholdNumber);
        Assert.Equal("Nguyen Van An", Assert.Single(result.Data.Residents).FullName);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero), result.Data.RegisteredAt);
        Assert.Equal(new DateTimeOffset(1980, 4, 20, 0, 0, 0, TimeSpan.Zero),
            result.Data.Residents[0].DateOfBirth);
        Assert.Equal(AuditAction.CreateHouseholdProfile, Assert.Single(_audit.Entries).Action);
    }

    [Fact]
    public async Task UpdateHousehold_UpdatesMatchingResident_AndRetainsResidentsNotSubmitted()
    {
        var apartment = _repository.SeedApartment("A-101");
        var household = _repository.SeedHousehold(apartment.Id, "HK-1");
        var existingResident = _repository.SeedResident(household, "Nguyen Van An", "001");
        _repository.SeedResident(household, "Nguyen Thi Binh", "002");

        var result = await _service.UpdateHouseholdAsync(
            1, "admin", household.Id, new UpdateHouseholdRequest
            {
                HouseholdNumber = "HK-1",
                HeadName = "Nguyen Van An",
                HouseNumber = "A-101",
                RegisteredAt = DateTimeOffset.Parse("2026-11-08T00:00:00+00:00"),
                Residents = [Resident(existingResident.Id, "Nguyen Van An", "2000")]
            });

        Assert.Equal(ProfileStatus.Success, result.Status);
        Assert.Equal(2, result.Data!.Residents.Count);
        Assert.Contains(result.Data.Residents, r => r.IdentityNumber == "2000");
        Assert.Contains(result.Data.Residents, r => r.IdentityNumber == "002");
        Assert.Equal(AuditAction.UpdateHouseholdProfile, Assert.Single(_audit.Entries).Action);
    }

    [Fact]
    public async Task UpdateHousehold_RejectsResidentFromDifferentHousehold()
    {
        var apartment = _repository.SeedApartment("A-101");
        var otherApartment = _repository.SeedApartment("A-102");
        var household = _repository.SeedHousehold(apartment.Id, "HK-1");
        var otherHousehold = _repository.SeedHousehold(otherApartment.Id, "HK-2");
        var otherResident = _repository.SeedResident(otherHousehold, "Other Resident", "003");

        var result = await _service.UpdateHouseholdAsync(
            1, "admin", household.Id, new UpdateHouseholdRequest
            {
                HouseholdNumber = "HK-1",
                HeadName = "Nguyen Van An",
                HouseNumber = "A-101",
                RegisteredAt = DateTimeOffset.UtcNow,
                Residents = [Resident(otherResident.Id, "Other Resident", "003")]
            });

        Assert.Equal(ProfileStatus.Invalid, result.Status);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    [Trait("TestId", "K2")]
    public async Task CreateApartment_SavesType_AndReturnsIt()
    {
        var result = await _service.CreateApartmentAsync(1, "admin", ApartmentRequest("K-101", ApartmentType.Kiot));

        Assert.Equal(ProfileStatus.Success, result.Status);
        Assert.Equal(ApartmentType.Kiot, result.Data!.Type);
    }

    [Fact]
    [Trait("TestId", "K2")]
    public async Task UpdateApartment_ChangesType()
    {
        var created = await _service.CreateApartmentAsync(1, "admin", ApartmentRequest("P-3001", ApartmentType.CanHo));

        var result = await _service.UpdateApartmentAsync(1, "admin", created.Data!.Id, new UpdateApartmentRequest
        {
            Code = "P-3001",
            Building = "A",
            Type = ApartmentType.Penthouse,
            Floor = 30,
            Area = 250m,
            BedroomCount = 4,
            Status = "Đang sử dụng"
        });

        Assert.Equal(ProfileStatus.Success, result.Status);
        Assert.Equal(ApartmentType.Penthouse, result.Data!.Type);
    }

    private static CreateApartmentRequest ApartmentRequest(string code, ApartmentType type = ApartmentType.CanHo) => new()
    {
        Code = code,
        Building = "A",
        Type = type,
        Floor = 1,
        Area = 68.5m,
        BedroomCount = 2,
        Status = "Đang sử dụng"
    };

    private static ResidentProfileRequest Resident(string name, DateTimeOffset dateOfBirth) => new()
    {
        FullName = name,
        DateOfBirth = dateOfBirth,
        Gender = "Nam",
        IdentityNumber = "001"
    };

    private static ResidentProfileRequest Resident(int id, string name, string identityNumber) => new()
    {
        Id = id,
        FullName = name,
        DateOfBirth = DateTimeOffset.Parse("1980-04-20T00:00:00+00:00"),
        Gender = "Nam",
        IdentityNumber = identityNumber
    };

    private sealed class FakeApartmentProfileRepository : IApartmentProfileRepository
    {
        private readonly List<ApartmentEntity> _apartments = [];
        private readonly List<HouseholdEntity> _households = [];
        private int _nextApartmentId = 1;
        private int _nextHouseholdId = 1;
        private int _nextResidentId = 1;

        public Task<List<ApartmentEntity>> GetApartmentsAsync(CancellationToken ct = default) =>
            Task.FromResult(_apartments);

        public Task<ApartmentEntity?> GetApartmentByIdAsync(int id, bool tracking, CancellationToken ct = default) =>
            Task.FromResult(_apartments.SingleOrDefault(a => a.Id == id));

        public Task<HouseholdEntity?> GetHouseholdByApartmentIdAsync(int apartmentId, CancellationToken ct = default) =>
            Task.FromResult(_households.SingleOrDefault(h => h.ApartmentId == apartmentId));

        public Task<HouseholdEntity?> GetHouseholdByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(_households.SingleOrDefault(h => h.Id == id));

        public Task<bool> ApartmentCodeExistsAsync(string code, int? exceptId = null, CancellationToken ct = default) =>
            Task.FromResult(_apartments.Any(a => a.Code == code && a.Id != exceptId));

        public Task<bool> HouseholdNumberExistsAsync(string number, int? exceptId = null, CancellationToken ct = default) =>
            Task.FromResult(_households.Any(h => h.HouseholdNumber == number && h.Id != exceptId));

        public Task<bool> ResidentIdentityExistsAsync(string identityNumber, int? exceptId = null, CancellationToken ct = default) =>
            Task.FromResult(_households.SelectMany(h => h.Residents)
                .Any(r => r.IdentityNumber == identityNumber && r.Id != exceptId));

        public Task AddApartmentAsync(ApartmentEntity apartment, CancellationToken ct = default)
        {
            apartment.Id = _nextApartmentId++;
            _apartments.Add(apartment);
            return Task.CompletedTask;
        }

        public Task AddHouseholdAsync(HouseholdEntity household, CancellationToken ct = default)
        {
            household.Id = _nextHouseholdId++;
            foreach (var resident in household.Residents)
                resident.Id = _nextResidentId++;
            _households.Add(household);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            foreach (var resident in _households.SelectMany(h => h.Residents).Where(r => r.Id == 0))
                resident.Id = _nextResidentId++;
            return Task.CompletedTask;
        }

        public ApartmentEntity SeedApartment(string code)
        {
            var apartment = new ApartmentEntity { Id = _nextApartmentId++, Code = code };
            _apartments.Add(apartment);
            return apartment;
        }

        public HouseholdEntity SeedHousehold(int apartmentId, string number)
        {
            var household = new HouseholdEntity
            {
                Id = _nextHouseholdId++,
                ApartmentId = apartmentId,
                HouseholdNumber = number
            };
            _households.Add(household);
            return household;
        }

        public ResidentEntity SeedResident(HouseholdEntity household, string name, string identityNumber)
        {
            var resident = new ResidentEntity
            {
                Id = _nextResidentId++,
                HouseholdId = household.Id,
                FullName = name,
                IdentityNumber = identityNumber,
                DateOfBirth = DateTimeOffset.UtcNow,
                Gender = "Nam"
            };
            household.Residents.Add(resident);
            return resident;
        }
    }
}
