using Assetra.Application.Assistant;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Assistant;

public class HybridFinancialAssistantTests
{
    private static RuleBasedFinancialAssistant Rules() =>
        new(new Mock<IBalanceQueryService>().Object);

    [Fact]
    public async Task Answer_RuleHandled_DoesNotCallLlm()
    {
        var balances = new Mock<IBalanceQueryService>();
        balances.Setup(b => b.GetAllCashBalancesAsync())
            .ReturnsAsync(new Dictionary<Guid, Money> { [Guid.NewGuid()] = new(100m, "TWD") });
        balances.Setup(b => b.GetAllLiabilitySnapshotsAsync())
            .ReturnsAsync(new Dictionary<string, LiabilitySnapshot>());
        var llm = new Mock<ILlmProvider>();
        llm.SetupGet(l => l.IsConfigured).Returns(true);

        var sut = new HybridFinancialAssistant(
            new RuleBasedFinancialAssistant(balances.Object), llm.Object);
        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("淨資產"));

        Assert.True(resp.IsHandled);
        llm.Verify(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Answer_UnknownQuery_LlmConfigured_FallsThrough()
    {
        var llm = new Mock<ILlmProvider>();
        llm.SetupGet(l => l.IsConfigured).Returns(true);
        llm.SetupGet(l => l.ProviderId).Returns("test-llm");
        llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("LLM-generated answer");

        var sut = new HybridFinancialAssistant(Rules(), llm.Object);
        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("天氣怎麼樣？"));

        Assert.True(resp.IsHandled);
        Assert.Equal("LLM-generated answer", resp.Answer);
        Assert.Equal("test-llm", resp.Source);
    }

    [Fact]
    public async Task Answer_UnknownQuery_LlmUnconfigured_ReturnsRuleUnhandled()
    {
        var llm = new Mock<ILlmProvider>();
        llm.SetupGet(l => l.IsConfigured).Returns(false);

        var sut = new HybridFinancialAssistant(Rules(), llm.Object);
        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("nonsense"));

        Assert.False(resp.IsHandled);
        llm.Verify(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Answer_UnknownQuery_LlmThrows_DegradesGracefully()
    {
        var llm = new Mock<ILlmProvider>();
        llm.SetupGet(l => l.IsConfigured).Returns(true);
        llm.SetupGet(l => l.ProviderId).Returns("flaky-llm");
        llm.Setup(l => l.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LlmProviderException("network down"));

        var sut = new HybridFinancialAssistant(Rules(), llm.Object);
        var resp = await sut.AnswerAsync(new FinancialAssistantQuery("nonsense"));

        Assert.False(resp.IsHandled);
        Assert.Contains("LLM 查詢失敗", resp.Answer);
        Assert.Contains("network down", resp.Answer);
    }
}
