using Assetra.Application.Tax;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Tax;

public class TaxCalculationServiceTests
{
    private static Trade Dividend(string symbol, string exchange, DateTime date, decimal cash) =>
        new(Guid.NewGuid(), symbol, exchange, symbol, TradeType.CashDividend, date, 1m, 1, null, null,
            CashAmount: cash);

    private static Trade Sell(string symbol, string exchange, DateTime date, decimal pnl) =>
        new(Guid.NewGuid(), symbol, exchange, symbol, TradeType.Sell, date, 100m, 10, pnl, 0m);

    private static Trade Buy(string symbol, string exchange, DateTime date) =>
        new(Guid.NewGuid(), symbol, exchange, symbol, TradeType.Buy, date, 100m, 10, null, null);

    [Fact]
    public void CalculateForYear_NoTrades_ReturnsZeroSummary()
    {
        var summary = TaxCalculationService.CalculateForYear(2026, Array.Empty<Trade>());

        Assert.Equal(2026, summary.Year);
        Assert.Equal(0m, summary.DomesticDividendTotal);
        Assert.Equal(0m, summary.OverseasDividendTotal);
        Assert.Equal(0m, summary.DomesticCapitalGainTotal);
        Assert.Equal(0m, summary.OverseasCapitalGainTotal);
        Assert.Equal(0m, summary.OverseasIncomeTotal);
        Assert.False(summary.TriggersAmtDeclaration);
        Assert.Empty(summary.Dividends);
        Assert.Empty(summary.CapitalGains);
    }

    [Fact]
    public void CalculateForYear_DomesticDividend_AggregatesUnderDomestic()
    {
        var trades = new[]
        {
            Dividend("2330", "TWSE", new DateTime(2026, 6, 1), 5_000m),
            Dividend("0050", "TPEX", new DateTime(2026, 9, 1), 3_000m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(8_000m, summary.DomesticDividendTotal);
        Assert.Equal(0m, summary.OverseasDividendTotal);
        Assert.Equal(2, summary.Dividends.Count);
        Assert.All(summary.Dividends, d => Assert.False(d.IsOverseas));
    }

    [Fact]
    public void CalculateForYear_OverseasDividend_AggregatesUnderOverseas()
    {
        var trades = new[]
        {
            Dividend("AAPL", "NASDAQ", new DateTime(2026, 5, 1), 200m),
            Dividend("0700", "HKEX", new DateTime(2026, 8, 1), 300m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(0m, summary.DomesticDividendTotal);
        Assert.Equal(500m, summary.OverseasDividendTotal);
        Assert.All(summary.Dividends, d => Assert.True(d.IsOverseas));
    }

    [Fact]
    public void CalculateForYear_MixedDividends_SplitByCountry()
    {
        var trades = new[]
        {
            Dividend("2330", "TWSE", new DateTime(2026, 6, 1), 5_000m),
            Dividend("AAPL", "NASDAQ", new DateTime(2026, 5, 1), 1_000m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(5_000m, summary.DomesticDividendTotal);
        Assert.Equal(1_000m, summary.OverseasDividendTotal);
    }

    [Fact]
    public void CalculateForYear_DomesticSell_GainsRecordedAsDomestic()
    {
        var trades = new[]
        {
            Sell("2330", "TWSE", new DateTime(2026, 7, 1), 50_000m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(50_000m, summary.DomesticCapitalGainTotal);
        Assert.Equal(0m, summary.OverseasCapitalGainTotal);
        var gain = Assert.Single(summary.CapitalGains);
        Assert.False(gain.IsOverseas);
        Assert.Equal("TW", gain.Country);
    }

    [Fact]
    public void CalculateForYear_OverseasSell_GainsRecordedAsOverseas()
    {
        var trades = new[]
        {
            Sell("AAPL", "NASDAQ", new DateTime(2026, 7, 1), 800_000m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(0m, summary.DomesticCapitalGainTotal);
        Assert.Equal(800_000m, summary.OverseasCapitalGainTotal);
        Assert.True(summary.CapitalGains[0].IsOverseas);
    }

    [Fact]
    public void CalculateForYear_FiltersOtherYears()
    {
        var trades = new[]
        {
            Dividend("2330", "TWSE", new DateTime(2025, 12, 31), 1_000m),
            Dividend("2330", "TWSE", new DateTime(2026, 1, 1), 2_000m),
            Dividend("2330", "TWSE", new DateTime(2027, 1, 1), 3_000m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(2_000m, summary.DomesticDividendTotal);
        Assert.Single(summary.Dividends);
    }

    [Fact]
    public void CalculateForYear_IgnoresNonTaxRelevantTradeTypes()
    {
        var trades = new[]
        {
            Buy("2330", "TWSE", new DateTime(2026, 1, 5)),
            // Sell with null RealizedPnl (e.g. 元交易導入時尚未填) — not aggregated
            new Trade(Guid.NewGuid(), "2330", "TWSE", "2330", TradeType.Sell,
                new DateTime(2026, 2, 5), 100m, 10, null, null),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Empty(summary.Dividends);
        Assert.Empty(summary.CapitalGains);
    }

    [Fact]
    public void CalculateForYear_OverseasIncomeAtThreshold_TriggersAmt()
    {
        var trades = new[]
        {
            Sell("AAPL", "NASDAQ", new DateTime(2026, 7, 1), TaxCalculationService.AmtDeclarationThreshold),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(TaxCalculationService.AmtDeclarationThreshold, summary.OverseasIncomeTotal);
        Assert.True(summary.TriggersAmtDeclaration);
    }

    [Fact]
    public void CalculateForYear_OverseasIncomeBelowThreshold_DoesNotTriggerAmt()
    {
        var trades = new[]
        {
            Sell("AAPL", "NASDAQ", new DateTime(2026, 7, 1),
                TaxCalculationService.AmtDeclarationThreshold - 1m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.False(summary.TriggersAmtDeclaration);
    }

    [Fact]
    public void CalculateForYear_AggregatesOverseasDividendAndGain_TogetherForAmt()
    {
        var trades = new[]
        {
            Dividend("AAPL", "NASDAQ", new DateTime(2026, 5, 1), 600_000m),
            Sell("AAPL", "NASDAQ", new DateTime(2026, 7, 1), 500_000m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(1_100_000m, summary.OverseasIncomeTotal);
        Assert.True(summary.TriggersAmtDeclaration);
    }

    [Fact]
    public void CalculateForYear_UnknownExchange_DefaultsToDomestic()
    {
        var trades = new[]
        {
            Dividend("MYSTERY", "UNKNOWN_VENUE", new DateTime(2026, 6, 1), 1_000m),
        };

        var summary = TaxCalculationService.CalculateForYear(2026, trades);

        Assert.Equal(1_000m, summary.DomesticDividendTotal);
        Assert.Equal(0m, summary.OverseasDividendTotal);
        Assert.False(summary.Dividends[0].IsOverseas);
        Assert.Equal("TW", summary.Dividends[0].Country);
    }

    [Fact]
    public void CalculateForYear_NullTrades_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TaxCalculationService.CalculateForYear(2026, null!));
    }
}
