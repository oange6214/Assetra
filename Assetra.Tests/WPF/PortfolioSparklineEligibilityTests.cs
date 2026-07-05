using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// Sparkline 載入清單決定「投資組合走勢圖」把哪些部位算進去。原本用 IsStock（只有
/// AssetType.Stock）會漏掉 ETF／基金／債券… —— 例如台股 ETF 00988A（AssetType.Etf）
/// 就沒 sparkline，選群組看走勢時整檔被漏算、金額嚴重短計（使用者實例：柏翰走勢只剩
/// DRAM、漏了 00988A）。改用 IsTradeableSecurity。若有人改回 IsStock，ETF/Fund/Bond
/// 這幾條 assert 會紅。
/// </summary>
public sealed class PortfolioSparklineEligibilityTests
{
    [Theory]
    [InlineData(AssetType.Stock, true)]
    [InlineData(AssetType.Etf, true)]           // ← 台股 ETF 00988A 這類：原本被 IsStock 擋掉
    [InlineData(AssetType.Fund, true)]
    [InlineData(AssetType.Bond, true)]
    [InlineData(AssetType.PreciousMetal, true)]
    [InlineData(AssetType.Crypto, true)]
    public void IsSparklineEligible_IncludesAllTradeableSecurities_NotJustStock(
        AssetType type, bool expected)
    {
        var row = new PortfolioRowViewModel { Symbol = "X", AssetType = type };

        Assert.Equal(expected, PortfolioViewModel.IsSparklineEligible(row));
    }

    [Fact]
    public void IsSparklineEligible_False_WhenSymbolMissing()
    {
        var row = new PortfolioRowViewModel { Symbol = "", AssetType = AssetType.Etf };

        Assert.False(PortfolioViewModel.IsSparklineEligible(row));
    }
}
