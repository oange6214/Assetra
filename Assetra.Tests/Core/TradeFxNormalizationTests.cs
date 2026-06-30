using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

/// <summary>
/// 防呆守門:<see cref="Trade.WithSameCurrencyFxCleared"/> 確保同幣別交易不會帶著 ≠null 的匯率。
/// 鎖定 6/21 00918 那類污染(TWD→TWD 卻存了 fx=0.0765,把實收金額算成 1/13)不會再從任何
/// 寫入路徑進到 DB —— 因為 TradeSqliteRepository.BindTradeParams 對每一筆寫入都呼叫它。
/// </summary>
public sealed class TradeFxNormalizationTests
{
    private static Trade Trade1(string instrumentCcy, string settlementCcy, decimal? fx) => new(
        Id: System.Guid.NewGuid(),
        Symbol: "00918",
        Exchange: "TWSE",
        Name: "test",
        Type: TradeType.Sell,
        TradeDate: new System.DateTime(2026, 6, 21),
        Price: 32.30m,
        Quantity: 19000,
        RealizedPnl: null,
        RealizedPnlPct: null,
        InstrumentCurrency: instrumentCcy,
        SettlementCurrency: settlementCcy,
        FxRate: fx,
        FxRateDate: fx is null ? null : new System.DateOnly(2026, 6, 21),
        FxSource: fx is null ? null : "manual");

    [Fact]
    public void SameCurrency_WithBogusFx_ClearsFxAndDateAndSource()
    {
        var t = Trade1("TWD", "TWD", 0.0765m).WithSameCurrencyFxCleared();

        Assert.Null(t.FxRate);
        Assert.Null(t.FxRateDate);
        Assert.Null(t.FxSource);
    }

    [Fact]
    public void SameCurrency_WithExplicitOne_NormalizesToNull()
    {
        // 同幣別的 fx=1 雖無害,但約定上應為 null,一併正規化。
        var t = Trade1("TWD", "TWD", 1m).WithSameCurrencyFxCleared();

        Assert.Null(t.FxRate);
    }

    [Fact]
    public void SameCurrency_CaseAndWhitespaceInsensitive_Clears()
    {
        var t = Trade1(" twd ", "TWD", 2.5m).WithSameCurrencyFxCleared();

        Assert.Null(t.FxRate);
    }

    [Fact]
    public void CrossCurrency_KeepsFx()
    {
        var t = Trade1("USD", "TWD", 31.59m).WithSameCurrencyFxCleared();

        Assert.Equal(31.59m, t.FxRate);
        Assert.Equal("manual", t.FxSource);
    }

    [Fact]
    public void SameCurrency_AlreadyClean_ReturnsEquivalent()
    {
        var t = Trade1("TWD", "TWD", null).WithSameCurrencyFxCleared();

        Assert.Null(t.FxRate);
        Assert.Null(t.FxRateDate);
    }
}
