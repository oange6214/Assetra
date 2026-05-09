using Moq;
using Assetra.Application.Assistant;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Assistant;

public class RuleBasedFinancialAssistantTests
{
    private static Mock<IBalanceQueryService> Balances(
        IReadOnlyDictionary<Guid, Money>? cash = null,
        IReadOnlyDictionary<string, LiabilitySnapshot>? liabilities = null)
    {
        var m = new Mock<IBalanceQueryService>();
        m.Setup(x => x.GetAllCashBalancesAsync())
            .ReturnsAsync(cash ?? new Dictionary<Guid, Money>());
        m.Setup(x => x.GetAllLiabilitySnapshotsAsync())
            .ReturnsAsync(liabilities ?? new Dictionary<string, LiabilitySnapshot>());
        return m;
    }

    [Fact]
    public async Task AnswerAsync_NetWorth_ComputesFromBalanceMinusLiability()
    {
        var cash = new Dictionary<Guid, Money>
        {
            [Guid.NewGuid()] = new(100_000m, "TWD"),
            [Guid.NewGuid()] = new(50_000m, "TWD"),
        };
        var liab = new Dictionary<string, LiabilitySnapshot>
        {
            ["loan A"] = new(new Money(30_000m, "TWD"), new Money(50_000m, "TWD")),
        };
        var sut = new RuleBasedFinancialAssistant(Balances(cash, liab).Object);

        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("我目前的淨資產是多少？"));

        Assert.True(resp.IsHandled);
        Assert.Contains("淨資產", resp.Answer);
        Assert.Contains("120,000", resp.Answer);  // 150k cash - 30k liab
        Assert.Equal(nameof(IBalanceQueryService), resp.Source);
    }

    [Fact]
    public async Task AnswerAsync_CashBalances_ListsAccounts()
    {
        var cash = new Dictionary<Guid, Money>
        {
            [Guid.NewGuid()] = new(80_000m, "TWD"),
            [Guid.NewGuid()] = new(20_000m, "USD"),
        };
        var sut = new RuleBasedFinancialAssistant(Balances(cash).Object);

        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("現金餘額"));

        Assert.True(resp.IsHandled);
        Assert.Contains("2 個現金帳戶", resp.Answer);
        Assert.Contains("80,000", resp.Answer);
        Assert.Contains("TWD", resp.Answer);
    }

    [Fact]
    public async Task AnswerAsync_Liabilities_ReportsRepaymentPercent()
    {
        var liab = new Dictionary<string, LiabilitySnapshot>
        {
            ["loan A"] = new(new Money(60_000m, "TWD"), new Money(100_000m, "TWD")),
        };
        var sut = new RuleBasedFinancialAssistant(Balances(liabilities: liab).Object);

        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("我的負債總額是多少？"));

        Assert.True(resp.IsHandled);
        Assert.Contains("已償還 40", resp.Answer);
    }

    [Fact]
    public async Task AnswerAsync_UnknownQuery_ReturnsUnhandled()
    {
        var sut = new RuleBasedFinancialAssistant(Balances().Object);

        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("天氣怎麼樣？"));

        Assert.False(resp.IsHandled);
        Assert.Contains("建議查詢範例", resp.Answer);
    }

    [Fact]
    public async Task AnswerAsync_EmptyInput_ReturnsUnhandled()
    {
        var sut = new RuleBasedFinancialAssistant(Balances().Object);

        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("   "));

        Assert.False(resp.IsHandled);
    }

    [Fact]
    public void SuggestedQueries_NotEmpty()
    {
        var sut = new RuleBasedFinancialAssistant(Balances().Object);
        Assert.NotEmpty(sut.SuggestedQueries);
    }
}
