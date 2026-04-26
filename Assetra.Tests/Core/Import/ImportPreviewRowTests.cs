using Assetra.Core.Models.Import;
using Xunit;

namespace Assetra.Tests.Core.Import;

public class ImportPreviewRowTests
{
    private static ImportPreviewRow Sample(
        decimal amount = -250m,
        string? counterparty = "Starbucks",
        string? memo = "Latte",
        string? symbol = null) =>
        new(
            RowIndex: 1,
            Date: new DateOnly(2026, 4, 26),
            Amount: amount,
            Counterparty: counterparty,
            Memo: memo,
            Symbol: symbol);

    [Fact]
    public void DedupeHash_IsStable_ForSameInputs()
    {
        var a = Sample();
        var b = Sample();
        Assert.Equal(a.DedupeHash, b.DedupeHash);
    }

    [Fact]
    public void DedupeHash_IsCaseAndWhitespaceInsensitive()
    {
        var a = Sample(counterparty: "Starbucks", memo: "Latte");
        var b = Sample(counterparty: "  STARBUCKS ", memo: " latte ");
        Assert.Equal(a.DedupeHash, b.DedupeHash);
    }

    [Fact]
    public void DedupeHash_Differs_WhenAmountDiffers()
    {
        var a = Sample(amount: -250m);
        var b = Sample(amount: -251m);
        Assert.NotEqual(a.DedupeHash, b.DedupeHash);
    }

    [Fact]
    public void DedupeHash_Differs_WhenSymbolDiffers()
    {
        var a = Sample(symbol: "2330");
        var b = Sample(symbol: "2317");
        Assert.NotEqual(a.DedupeHash, b.DedupeHash);
    }

    [Fact]
    public void DedupeHash_TreatsNullAndEmpty_TheSame()
    {
        var a = Sample(counterparty: null, memo: null);
        var b = Sample(counterparty: "", memo: "   ");
        Assert.Equal(a.DedupeHash, b.DedupeHash);
    }
}
