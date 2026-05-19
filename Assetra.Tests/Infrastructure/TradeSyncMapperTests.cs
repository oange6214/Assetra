using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class TradeSyncMapperTests
{
    private static Trade Sample() => new(
        Id: Guid.NewGuid(),
        Symbol: "2330",
        Exchange: "TWSE",
        Name: "台積電",
        Type: TradeType.Buy,
        TradeDate: DateTime.Parse("2026-04-01T00:00:00Z").ToUniversalTime(),
        Price: 800.5m,
        Quantity: 1000,
        RealizedPnl: 12.34m,
        RealizedPnlPct: 0.05m,
        CashAmount: 800500m,
        CashAccountId: Guid.NewGuid(),
        Note: "備註",
        Commission: 50.25m,
        LoanLabel: null,
        ToCashAccountId: null);

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var t = Sample();
        var env = TradeSyncMapper.ToEnvelope(
            t,
            new EntityVersion(3, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);
        var back = TradeSyncMapper.FromPayload(env);

        Assert.Equal(t.Id, back.Id);
        Assert.Equal(t.Symbol, back.Symbol);
        Assert.Equal(t.Name, back.Name);
        Assert.Equal(t.Type, back.Type);
        Assert.Equal(t.Price, back.Price);
        Assert.Equal(t.Quantity, back.Quantity);
        Assert.Equal(t.CashAmount, back.CashAmount);
        Assert.Equal(t.CashAccountId, back.CashAccountId);
        Assert.Equal(t.Note, back.Note);
        Assert.Equal(t.Commission, back.Commission);
        Assert.Equal(t.RealizedPnl, back.RealizedPnl);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var env = TradeSyncMapper.ToEnvelope(
            Sample(),
            new EntityVersion(2, DateTimeOffset.UtcNow, "dev"),
            isDeleted: true);

        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal("Trade", env.EntityType);
    }

    [Fact]
    public void FromPayload_ThrowsOnTombstone()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Trade", string.Empty,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: true);
        Assert.Throws<InvalidOperationException>(() => TradeSyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_ThrowsOnWrongEntityType()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Category", "{}",
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: false);
        Assert.Throws<ArgumentException>(() => TradeSyncMapper.FromPayload(env));
    }

    [Fact]
    public void RoundTrip_PreservesMultiCurrencyFields()
    {
        // MultiCurrency-Trade-Refactor P1：複委託情境（USD 標的 + TWD 帳戶 + FxRate）。
        // Verifies the three new fields survive ToEnvelope/FromPayload round-trip.
        var t = Sample() with
        {
            Symbol = "AAPL",
            Exchange = "NASDAQ",
            InstrumentCurrency = "USD",
            CommissionCurrency = "TWD",       // 富邦複委託：手續費以 TWD 計算
            FxRate = 31.5m,                   // 1 USD = 31.5 TWD
            Price = 200m,                     // USD per share
            Quantity = 10,
            CashAmount = 63_500m,             // TWD actual debit
        };

        var env = TradeSyncMapper.ToEnvelope(
            t,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);
        var back = TradeSyncMapper.FromPayload(env);

        Assert.Equal("USD", back.InstrumentCurrency);
        Assert.Equal("TWD", back.CommissionCurrency);
        Assert.Equal(31.5m, back.FxRate);
        Assert.Equal(200m, back.Price);
        Assert.Equal(63_500m, back.CashAmount);
    }

    [Fact]
    public void FromPayload_BackCompat_OldPayloadWithoutMultiCurrencyFields()
    {
        // 舊 cloud payload（未含 instrument_currency / commission_currency / fx_rate）
        // 應該 decode 成 InstrumentCurrency="TWD" + 其餘為 null，向下相容。
        var legacyJson = """
            {
              "id": "00000000-0000-0000-0000-000000000001",
              "symbol": "2330",
              "exchange": "TWSE",
              "name": "台積電",
              "trade_type": "Buy",
              "trade_date": "2026-04-01T00:00:00.0000000Z",
              "price": "800.5",
              "quantity": 1000,
              "realized_pnl": null,
              "realized_pnl_pct": null,
              "cash_amount": "800500",
              "cash_account_id": null,
              "note": null,
              "portfolio_entry_id": null,
              "commission": null,
              "commission_discount": null,
              "loan_label": null,
              "principal": null,
              "interest_paid": null,
              "to_cash_account_id": null,
              "liability_asset_id": null,
              "parent_trade_id": null,
              "category_id": null,
              "recurring_source_id": null
            }
            """;
        var env = new SyncEnvelope(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "Trade", legacyJson,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: false);

        var back = TradeSyncMapper.FromPayload(env);

        Assert.Equal("TWD", back.InstrumentCurrency);
        Assert.Null(back.CommissionCurrency);
        Assert.Null(back.FxRate);
        Assert.Equal(800.5m, back.Price);
    }

    [Fact]
    public void Payload_UsesSnakeCaseAndUnescapedCjk()
    {
        var t = Sample();
        var env = TradeSyncMapper.ToEnvelope(
            t,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);

        Assert.Contains("\"trade_type\"", env.PayloadJson);
        Assert.Contains("\"cash_account_id\"", env.PayloadJson);
        Assert.Contains("台積電", env.PayloadJson); // not escaped
    }
}
