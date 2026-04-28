using Assetra.Application.MultiAsset;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Xunit;

namespace Assetra.Tests.Application.MultiAsset;

public class RealEstateValuationServiceTests
{
    private static RealEstate MakeProperty(
        string name,
        decimal currentValue,
        decimal mortgage = 0m,
        bool isRental = false,
        RealEstateStatus status = RealEstateStatus.Active) =>
        new(Guid.NewGuid(), name, "台北市", 1_000_000m,
            new DateOnly(2020, 1, 1), currentValue, mortgage, "TWD",
            isRental, status, null, new EntityVersion());

    private static RentalIncomeRecord MakeRental(Guid propertyId, DateOnly month, decimal rent, decimal expenses = 0m) =>
        new(Guid.NewGuid(), propertyId, month, rent, expenses, "TWD", null);

    [Fact]
    public async Task GetTotalCurrentValue_ActiveOnly()
    {
        var active = MakeProperty("Active", 5_000_000m);
        var sold = MakeProperty("Sold", 3_000_000m, status: RealEstateStatus.Sold);
        var repo = new StubRealEstateRepository([active, sold]);
        var svc = new RealEstateValuationService(repo, new StubRentalRepo([]));

        var result = await svc.GetTotalCurrentValueAsync();

        Assert.Equal(5_000_000m, result);
    }

    [Fact]
    public async Task GetTotalEquity_DeductsMortgage()
    {
        var prop = MakeProperty("House", 8_000_000m, mortgage: 3_000_000m);
        var svc = new RealEstateValuationService(
            new StubRealEstateRepository([prop]),
            new StubRentalRepo([]));

        var result = await svc.GetTotalEquityAsync();

        Assert.Equal(5_000_000m, result);
    }

    [Fact]
    public async Task GetValuationSummaries_NonRental_HasZeroMonthlyNet()
    {
        var prop = MakeProperty("Non-Rental", 5_000_000m, isRental: false);
        var svc = new RealEstateValuationService(
            new StubRealEstateRepository([prop]),
            new StubRentalRepo([]));

        var summaries = await svc.GetValuationSummariesAsync();

        Assert.Single(summaries);
        Assert.Equal(0m, summaries[0].MonthlyNetRental);
    }

    [Fact]
    public async Task GetValuationSummaries_RentalProperty_ReturnsLatestMonthlyNet()
    {
        var prop = MakeProperty("Rental", 5_000_000m, isRental: true);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var lastMonth = today.AddMonths(-1);
        var rentals = new List<RentalIncomeRecord>
        {
            MakeRental(prop.Id, lastMonth, 30_000m, expenses: 5_000m),
        };
        var svc = new RealEstateValuationService(
            new StubRealEstateRepository([prop]),
            new StubRentalRepo(rentals));

        var summaries = await svc.GetValuationSummariesAsync();

        Assert.Equal(25_000m, summaries[0].MonthlyNetRental);
    }

    [Fact]
    public async Task GetTotalCurrentValue_EmptyRepo_ReturnsZero()
    {
        var svc = new RealEstateValuationService(
            new StubRealEstateRepository([]),
            new StubRentalRepo([]));

        Assert.Equal(0m, await svc.GetTotalCurrentValueAsync());
    }

    // ── Stubs ──

    private sealed class StubRealEstateRepository(IReadOnlyList<RealEstate> data) : IRealEstateRepository
    {
        public Task<IReadOnlyList<RealEstate>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<RealEstate?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(RealEstate entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RealEstate entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubRentalRepo(IReadOnlyList<RentalIncomeRecord> data) : IRentalIncomeRecordRepository
    {
        public Task<IReadOnlyList<RentalIncomeRecord>> GetByPropertyAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RentalIncomeRecord>>(data.Where(r => r.RealEstateId == id).ToList());
        public Task<IReadOnlyList<RentalIncomeRecord>> GetByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RentalIncomeRecord>>(data.Where(r => r.Month >= from && r.Month <= to).ToList());
        public Task AddAsync(RentalIncomeRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RentalIncomeRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
