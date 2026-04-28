using Assetra.Application.MultiAsset;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Xunit;

namespace Assetra.Tests.Application.MultiAsset;

public class RetirementProjectionServiceTests
{
    private static RetirementAccount MakeAccount(
        string name,
        decimal balance,
        int withdrawalAge = 65,
        RetirementAccountStatus status = RetirementAccountStatus.Active) =>
        new(Guid.NewGuid(), name, RetirementAccountType.LaborPension, "Bureau",
            balance, 0.06m, 0.06m, 5, withdrawalAge,
            new DateOnly(2020, 1, 1), "TWD", status, null, new EntityVersion());

    [Fact]
    public async Task GetTotalBalance_ActiveOnly()
    {
        var active = MakeAccount("Active", 1_000_000m);
        var closed = MakeAccount("Closed", 500_000m, status: RetirementAccountStatus.Closed);
        var svc = new RetirementProjectionService(
            new StubAccountRepo([active, closed]),
            new StubContribRepo([]));

        Assert.Equal(1_000_000m, await svc.GetTotalBalanceAsync());
    }

    [Fact]
    public async Task ProjectAsync_ZeroRate_LinearAccumulation()
    {
        var acc = MakeAccount("A", 100_000m, withdrawalAge: 65);
        var svc = new RetirementProjectionService(
            new StubAccountRepo([acc]),
            new StubContribRepo([]));

        var p = await svc.ProjectAsync(acc.Id, currentAge: 60, annualReturnRate: 0m, annualContribution: 50_000m);

        Assert.NotNull(p);
        Assert.Equal(5, p!.YearsToWithdrawal);
        Assert.Equal(250_000m, p.TotalContributions);
        Assert.Equal(350_000m, p.ProjectedBalance);
    }

    [Fact]
    public async Task ProjectAsync_PositiveRate_CompoundsCorrectly()
    {
        var acc = MakeAccount("A", 100_000m, withdrawalAge: 62);
        var svc = new RetirementProjectionService(
            new StubAccountRepo([acc]),
            new StubContribRepo([]));

        var p = await svc.ProjectAsync(acc.Id, currentAge: 60, annualReturnRate: 0.10m, annualContribution: 0m);

        Assert.NotNull(p);
        // 100000 * 1.1^2 = 121000
        Assert.Equal(121_000m, Math.Round(p!.ProjectedBalance, 2));
    }

    [Fact]
    public async Task ProjectAsync_UnknownAccount_ReturnsNull()
    {
        var svc = new RetirementProjectionService(
            new StubAccountRepo([]),
            new StubContribRepo([]));

        Assert.Null(await svc.ProjectAsync(Guid.NewGuid(), 30, 0.05m, 0m));
    }

    [Fact]
    public async Task GetAccountSummaries_ReturnsLatestYearContribution()
    {
        var acc = MakeAccount("A", 100_000m);
        var contribs = new List<RetirementContribution>
        {
            new(Guid.NewGuid(), acc.Id, 2024, 50_000m, 50_000m, "TWD", null),
            new(Guid.NewGuid(), acc.Id, 2025, 60_000m, 60_000m, "TWD", null),
        };
        var svc = new RetirementProjectionService(
            new StubAccountRepo([acc]),
            new StubContribRepo(contribs));

        var summaries = await svc.GetAccountSummariesAsync();

        Assert.Single(summaries);
        Assert.Equal(120_000m, summaries[0].LatestYearContribution);
    }

    private sealed class StubAccountRepo(IReadOnlyList<RetirementAccount> data) : IRetirementAccountRepository
    {
        public Task<IReadOnlyList<RetirementAccount>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<RetirementAccount?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(RetirementAccount entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RetirementAccount entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubContribRepo(IReadOnlyList<RetirementContribution> data) : IRetirementContributionRepository
    {
        public Task<IReadOnlyList<RetirementContribution>> GetByAccountAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RetirementContribution>>(data.Where(r => r.AccountId == accountId).ToList());
        public Task<IReadOnlyList<RetirementContribution>> GetByYearAsync(int year, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RetirementContribution>>(data.Where(r => r.Year == year).ToList());
        public Task AddAsync(RetirementContribution record, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RetirementContribution record, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
