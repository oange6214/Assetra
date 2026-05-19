using Assetra.Application.Portfolio.Services;
using Xunit;

namespace Assetra.Tests.Application.Portfolio;

/// <summary>
/// Covers <see cref="AddAssetWorkflowService.InferExchange"/> — the heuristic that
/// picks an exchange when the user doesn't go through the autocomplete pipeline.
/// Constructor args are all null because <c>InferExchange</c> doesn't touch any
/// of the injected dependencies (pure function over the symbol string).
/// </summary>
public class InferExchangeTests
{
    private readonly AddAssetWorkflowService _svc = new(
        searchService: new NullSymbolSearchService());

    [Theory]
    // ── Taiwan exchanges (digits dominant) ────────────────────────────
    [InlineData("2330", "TWSE")]
    [InlineData("0050", "TWSE")]
    [InlineData("2882", "TWSE")]
    [InlineData("1101", "TWSE")]
    [InlineData("00981A", "TPEX")]
    [InlineData("00982A", "TPEX")]
    // ── US — pure letters 1–5, with or without .X class suffix ────────
    [InlineData("F", "NASDAQ")]          // Ford
    [InlineData("T", "NASDAQ")]          // AT&T
    [InlineData("V", "NASDAQ")]          // Visa
    [InlineData("SPY", "NASDAQ")]        // SPDR ETF
    [InlineData("VTI", "NASDAQ")]        // Vanguard ETF
    [InlineData("QQQ", "NASDAQ")]        // Invesco ETF
    [InlineData("AAPL", "NASDAQ")]
    [InlineData("NVDA", "NASDAQ")]
    [InlineData("MSFT", "NASDAQ")]
    [InlineData("META", "NASDAQ")]
    [InlineData("TSLA", "NASDAQ")]
    [InlineData("GOOGL", "NASDAQ")]      // 5 letters edge
    [InlineData("DRAM", "NASDAQ")]       // Roundhill ETF — the original bug case
    [InlineData("TSM", "NASDAQ")]        // TSMC ADR (NYSE actually, but NASDAQ tag is fine — see InferExchange XML doc)
    [InlineData("BRK.B", "NASDAQ")]      // Class B shares
    [InlineData("BF.B", "NASDAQ")]       // Brown-Forman Class B
    // ── Whitespace + casing robustness ────────────────────────────────
    [InlineData("aapl", "NASDAQ")]
    [InlineData("  AAPL  ", "NASDAQ")]
    [InlineData("  2330", "TWSE")]
    // ── Conservative fallback for unknown / mixed formats ─────────────
    [InlineData("", "TWSE")]
    [InlineData("   ", "TWSE")]
    [InlineData("ABCDEF", "TWSE")]       // 6 letters — out of common-ticker range, conservative
    [InlineData("123A", "TWSE")]         // digits + letters, not a Taiwan pattern either
    public void InferExchange_RoutesCorrectly(string symbol, string expected)
        => Assert.Equal(expected, _svc.InferExchange(symbol));
}

/// <summary>
/// Minimal <see cref="Assetra.Core.Interfaces.IStockSearchService"/> stub.
/// InferExchange doesn't call it, but the AddAssetWorkflowService ctor needs one.
/// </summary>
file sealed class NullSymbolSearchService : Assetra.Core.Interfaces.IStockSearchService
{
    public IReadOnlyList<Assetra.Core.Models.StockSearchResult> Search(string query) => [];
    public IReadOnlyList<Assetra.Core.Models.StockSearchResult> GetAll() => [];
    public string? GetExchange(string symbol) => null;
    public string? GetName(string symbol) => null;
    public string? GetSector(string symbol) => null;
    public bool IsEtf(string symbol) => false;
    public bool IsBondEtf(string symbol) => false;
}
