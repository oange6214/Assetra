using Moq;
using Assetra.Application.Assistant;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Assistant;

public class RuleBasedAssistantInsightServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 9, 0, 0, 0, DateTimeKind.Utc);
    private static TimeProvider FixedTime() =>
        new TestTimeProvider(FixedNow);

    [Fact]
    public async Task BudgetOverspending_Critical_WhenRatioOver100Percent()
    {
        var categoryId = Guid.NewGuid();
        var budgets = new Mock<IBudgetRepository>();
        budgets.Setup(b => b.GetByPeriodAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Budget(Guid.NewGuid(), categoryId, BudgetMode.Monthly, 2026, 5, 5_000m)]);

        var trades = new Mock<ITradeRepository>();
        trades.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Trade(Guid.NewGuid(), "", "", "Food", TradeType.Withdrawal,
                    new DateTime(2026, 5, 1), 0m, 1, null, null,
                    CashAmount: 6_000m, CategoryId: categoryId)]);

        var sut = new RuleBasedAssistantInsightService(budgets.Object, null, trades.Object, FixedTime());
        var insights = await sut.GetCurrentInsightsAsync();

        var insight = Assert.Single(insights);
        Assert.Equal(AssistantInsightSeverity.Critical, insight.Severity);
        Assert.Contains("超支", insight.Title);
    }

    [Fact]
    public async Task BudgetWarning_When80To100Percent()
    {
        var categoryId = Guid.NewGuid();
        var budgets = new Mock<IBudgetRepository>();
        budgets.Setup(b => b.GetByPeriodAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Budget(Guid.NewGuid(), categoryId, BudgetMode.Monthly, 2026, 5, 5_000m)]);

        var trades = new Mock<ITradeRepository>();
        trades.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Trade(Guid.NewGuid(), "", "", "Food", TradeType.Withdrawal,
                    new DateTime(2026, 5, 5), 0m, 1, null, null,
                    CashAmount: 4_500m, CategoryId: categoryId)]);

        var sut = new RuleBasedAssistantInsightService(budgets.Object, null, trades.Object, FixedTime());
        var insights = await sut.GetCurrentInsightsAsync();

        Assert.Equal(AssistantInsightSeverity.Warning, Assert.Single(insights).Severity);
    }

    [Fact]
    public async Task RecurringUpcoming_ListsItemsDueWithin7Days()
    {
        var recurring = new Mock<IRecurringTransactionRepository>();
        recurring.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RecurringTransaction(
                    Id: Guid.NewGuid(), Name: "Spotify", TradeType: TradeType.Withdrawal,
                    Amount: 149m, CashAccountId: null, CategoryId: null,
                    Frequency: RecurrenceFrequency.Monthly, Interval: 1,
                    StartDate: new DateTime(2025, 1, 1), EndDate: null,
                    GenerationMode: AutoGenerationMode.AutoApply, LastGeneratedAt: null,
                    NextDueAt: new DateTime(2026, 5, 12)),  // 3 days from FixedNow=5/9
                new RecurringTransaction(
                    Id: Guid.NewGuid(), Name: "Far Future", TradeType: TradeType.Withdrawal,
                    Amount: 500m, CashAccountId: null, CategoryId: null,
                    Frequency: RecurrenceFrequency.Monthly, Interval: 1,
                    StartDate: new DateTime(2025, 1, 1), EndDate: null,
                    GenerationMode: AutoGenerationMode.AutoApply, LastGeneratedAt: null,
                    NextDueAt: new DateTime(2026, 6, 1)),  // way out
            ]);

        var sut = new RuleBasedAssistantInsightService(null, recurring.Object, null, FixedTime());
        var insights = await sut.GetCurrentInsightsAsync();

        var insight = Assert.Single(insights);
        Assert.Equal(AssistantInsightSeverity.Info, insight.Severity);
        Assert.Contains("Spotify", insight.Title);
    }

    [Fact]
    public async Task GetCurrentInsightsAsync_NoServices_ReturnsEmpty()
    {
        var sut = new RuleBasedAssistantInsightService(null, null, null, FixedTime());
        var insights = await sut.GetCurrentInsightsAsync();
        Assert.Empty(insights);
    }

    [Fact]
    public async Task Insights_SortedBySeverityDescending()
    {
        var categoryId = Guid.NewGuid();
        var budgets = new Mock<IBudgetRepository>();
        budgets.Setup(b => b.GetByPeriodAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Budget(Guid.NewGuid(), categoryId, BudgetMode.Monthly, 2026, 5, 5_000m)]);
        var trades = new Mock<ITradeRepository>();
        trades.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Trade(Guid.NewGuid(), "", "", "Food", TradeType.Withdrawal,
                    new DateTime(2026, 5, 1), 0m, 1, null, null,
                    CashAmount: 6_000m, CategoryId: categoryId)]);
        var recurring = new Mock<IRecurringTransactionRepository>();
        recurring.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RecurringTransaction(Guid.NewGuid(), "Spotify", TradeType.Withdrawal,
                    149m, null, null, RecurrenceFrequency.Monthly, 1,
                    new DateTime(2025, 1, 1), null,
                    AutoGenerationMode.AutoApply, null,
                    NextDueAt: new DateTime(2026, 5, 12))]);

        var sut = new RuleBasedAssistantInsightService(budgets.Object, recurring.Object, trades.Object, FixedTime());
        var insights = await sut.GetCurrentInsightsAsync();

        Assert.Equal(2, insights.Count);
        Assert.Equal(AssistantInsightSeverity.Critical, insights[0].Severity);
        Assert.Equal(AssistantInsightSeverity.Info, insights[1].Severity);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTimeProvider(DateTime now) => _now = new DateTimeOffset(now, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
