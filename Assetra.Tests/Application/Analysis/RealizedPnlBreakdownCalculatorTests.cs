using Assetra.Application.Analysis;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

/// <summary>
/// MultiCurrency-Reporting P4.5 — verifies the market vs FX split formula.
/// </summary>
public sealed class RealizedPnlBreakdownCalculatorTests
{
    [Fact]
    public void Compute_AaplExample_MatchesSpec()
    {
        // From docs/planning/MultiCurrency-Reporting.md P4.5:
        // Bought AAPL $100/share when USD=30 TWD, sold $110/share when USD=32 TWD, 100 shares.
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 110m,
            buyAvgPriceNative: 100m,
            quantity: 100,
            buyFxRate: 30m,
            sellFxRate: 32m);

        Assert.NotNull(result);
        Assert.Equal(1_000m, result!.MarketNative);      // ($110 − $100) × 100 = $1,000
        Assert.Equal(32_000m, result.MarketBase);        // $1,000 × 32 = 32,000 TWD
        Assert.Equal(20_000m, result.FxBase);            // $10,000 × (32 − 30) = 20,000 TWD
        Assert.Equal(52_000m, result.TotalBase);         // 32,000 + 20,000
    }

    [Fact]
    public void Compute_SameCurrencyTrade_FxBaseIsZero()
    {
        // TWD-denominated stock, both fx rates 1.0 (or both null) → fx component 0.
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 600m,
            buyAvgPriceNative: 550m,
            quantity: 1_000,
            buyFxRate: 1m,
            sellFxRate: 1m);

        Assert.NotNull(result);
        Assert.Equal(50_000m, result!.MarketNative);
        Assert.Equal(50_000m, result.MarketBase);
        Assert.Equal(0m, result.FxBase);
        Assert.Equal(50_000m, result.TotalBase);
    }

    [Fact]
    public void Compute_BothFxNull_TreatsAsSameCurrency()
    {
        // Caller passing nulls for both = explicit "same currency" intent.
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 600m,
            buyAvgPriceNative: 550m,
            quantity: 1_000,
            buyFxRate: null,
            sellFxRate: null);

        Assert.NotNull(result);
        Assert.Equal(0m, result!.FxBase);
        Assert.Equal(50_000m, result.TotalBase);
    }

    [Fact]
    public void Compute_OnlyBuyFxAvailable_ReturnsNull()
    {
        // Mixed-currency case with one fx rate missing → caller renders "—".
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 110m,
            buyAvgPriceNative: 100m,
            quantity: 100,
            buyFxRate: 30m,
            sellFxRate: null);

        Assert.Null(result);
    }

    [Fact]
    public void Compute_OnlySellFxAvailable_ReturnsNull()
    {
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 110m,
            buyAvgPriceNative: 100m,
            quantity: 100,
            buyFxRate: null,
            sellFxRate: 32m);

        Assert.Null(result);
    }

    [Fact]
    public void Compute_ZeroQuantity_ReturnsNull()
    {
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 110m,
            buyAvgPriceNative: 100m,
            quantity: 0,
            buyFxRate: 30m,
            sellFxRate: 32m);

        Assert.Null(result);
    }

    [Fact]
    public void Compute_NegativeQuantity_ReturnsNull()
    {
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 110m,
            buyAvgPriceNative: 100m,
            quantity: -10,
            buyFxRate: 30m,
            sellFxRate: 32m);

        Assert.Null(result);
    }

    [Fact]
    public void Compute_MarketLossButFxGain_BothComponentsCorrect()
    {
        // Bought AAPL $100 @ USD=30, sold $95 @ USD=33. Lost on stock, won on FX.
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 95m,
            buyAvgPriceNative: 100m,
            quantity: 100,
            buyFxRate: 30m,
            sellFxRate: 33m);

        Assert.NotNull(result);
        Assert.Equal(-500m, result!.MarketNative);       // (95-100) × 100
        Assert.Equal(-16_500m, result.MarketBase);       // -500 × 33
        Assert.Equal(30_000m, result.FxBase);            // $10,000 × (33-30)
        Assert.Equal(13_500m, result.TotalBase);         // -16,500 + 30,000
        // Investor lost on the stock but FX bailed them out — exactly the
        // signal P4.5 is designed to surface.
    }

    [Fact]
    public void Compute_MarketGainAndFxLoss_BothComponentsCorrect()
    {
        // Bought AAPL $100 @ USD=33, sold $110 @ USD=30. Stock won, FX hurt.
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 110m,
            buyAvgPriceNative: 100m,
            quantity: 100,
            buyFxRate: 33m,
            sellFxRate: 30m);

        Assert.NotNull(result);
        Assert.Equal(1_000m, result!.MarketNative);
        Assert.Equal(30_000m, result.MarketBase);        // 1,000 × 30
        Assert.Equal(-30_000m, result.FxBase);           // $10,000 × (30-33)
        Assert.Equal(0m, result.TotalBase);              // Even after market win — FX wiped it
    }

    [Fact]
    public void Compute_TotalBase_AlwaysEqualsMarketBasePlusFxBase()
    {
        // Property test: the math should be internally consistent regardless of
        // input combination.
        var result = RealizedPnlBreakdownCalculator.Compute(
            sellPriceNative: 87.45m,
            buyAvgPriceNative: 79.20m,
            quantity: 250,
            buyFxRate: 28.55m,
            sellFxRate: 30.92m);

        Assert.NotNull(result);
        Assert.Equal(result!.MarketBase + result.FxBase, result.TotalBase);
    }
}
