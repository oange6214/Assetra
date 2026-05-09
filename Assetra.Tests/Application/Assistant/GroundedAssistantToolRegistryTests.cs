using Moq;
using Assetra.Application.Assistant;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Assistant;

public class GroundedAssistantToolRegistryTests
{
    [Fact]
    public async Task GetNetWorthTool_AggregatesCashMinusLiabilities()
    {
        var balances = new Mock<IBalanceQueryService>();
        balances.Setup(b => b.GetAllCashBalancesAsync())
            .ReturnsAsync(new Dictionary<Guid, Money>
            {
                [Guid.NewGuid()] = new(100_000m, "TWD"),
                [Guid.NewGuid()] = new(50_000m, "TWD"),
            });
        balances.Setup(b => b.GetAllLiabilitySnapshotsAsync())
            .ReturnsAsync(new Dictionary<string, LiabilitySnapshot>
            {
                ["loan A"] = new(new Money(30_000m, "TWD"), new Money(50_000m, "TWD")),
            });

        var sut = new GroundedAssistantToolRegistry(balances.Object);
        var tool = sut.Find("get_net_worth");
        Assert.NotNull(tool);
        var result = await tool!.InvokeAsync(default);

        Assert.Contains("net_worth=120,000", result);
        Assert.Contains("cash=150,000", result);
        Assert.Contains("liabilities=30,000", result);
    }

    [Fact]
    public async Task ListLiabilitiesTool_EmptyState()
    {
        var balances = new Mock<IBalanceQueryService>();
        balances.Setup(b => b.GetAllLiabilitySnapshotsAsync())
            .ReturnsAsync(new Dictionary<string, LiabilitySnapshot>());

        var sut = new GroundedAssistantToolRegistry(balances.Object);
        var result = await sut.Find("list_liabilities")!.InvokeAsync(default);

        Assert.Equal("No outstanding liabilities", result);
    }

    [Fact]
    public void Find_UnknownName_ReturnsNull()
    {
        var sut = new GroundedAssistantToolRegistry(new Mock<IBalanceQueryService>().Object);
        Assert.Null(sut.Find("nonexistent_tool"));
    }

    [Fact]
    public void Tools_RegisteredWithoutBudgetRepo_ExcludesBudgetTool()
    {
        var sut = new GroundedAssistantToolRegistry(new Mock<IBalanceQueryService>().Object);
        Assert.Null(sut.Find("get_current_month_budgets"));
        Assert.NotNull(sut.Find("get_net_worth"));
    }

    [Fact]
    public void Tools_RegisteredWithBudgetRepo_IncludesBudgetTool()
    {
        var sut = new GroundedAssistantToolRegistry(
            new Mock<IBalanceQueryService>().Object,
            new Mock<IBudgetRepository>().Object);
        Assert.NotNull(sut.Find("get_current_month_budgets"));
    }
}
