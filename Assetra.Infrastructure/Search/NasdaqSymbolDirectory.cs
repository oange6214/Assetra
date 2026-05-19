using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Search;

public sealed class NasdaqSymbolDirectory : IRefreshableSymbolDirectory
{
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromDays(7);

    private readonly string _cacheDirectory;
    private readonly HttpClient _http;
    private IReadOnlyList<StockSearchResult> _symbols = [];

    public NasdaqSymbolDirectory(string cacheDirectory, HttpClient http)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(http);

        _cacheDirectory = cacheDirectory;
        _http = http;
        Reload();
    }

    internal NasdaqSymbolDirectory(string nasdaqListedContent, string otherListedContent)
    {
        _cacheDirectory = string.Empty;
        _http = new HttpClient();
        _symbols = Merge(
            NasdaqSymbolListParser.ParseNasdaqListed(nasdaqListedContent),
            NasdaqSymbolListParser.ParseOtherListed(otherListedContent));
    }

    public string SourceName => "Nasdaq Trader";
    public int Count => _symbols.Count;
    public DateTimeOffset? LastUpdatedAt => ResolveLastUpdatedAt();

    public IReadOnlyList<StockSearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var normalizedQuery = EquitySymbolNormalizer.ToSearchKey(query);
        var rawQuery = query.Trim();

        return _symbols
            .Where(s => EquitySymbolNormalizer.ToSearchKey(s.Symbol).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains(rawQuery, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => string.Equals(EquitySymbolNormalizer.ToSearchKey(s.Symbol), normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.Symbol)
            .ThenBy(s => s.Exchange)
            .Take(25)
            .ToList();
    }

    public StockSearchResult? Resolve(string symbol, string? exchange = null)
    {
        var canonical = EquitySymbolNormalizer.NormalizeCanonicalSymbol(symbol);
        var normalizedExchange = EquitySymbolNormalizer.NormalizeExchange(exchange ?? string.Empty);
        if (canonical.Length == 0)
            return null;

        var matches = _symbols.Where(s => EquitySymbolNormalizer.SymbolMatches(s.Symbol, canonical));

        if (normalizedExchange.Length > 0)
        {
            matches = matches.Where(s =>
                string.Equals(s.Exchange, normalizedExchange, StringComparison.OrdinalIgnoreCase));
        }

        return matches
            .OrderBy(s => ExchangeRank(s.Exchange))
            .ThenBy(s => s.Exchange)
            .FirstOrDefault();
    }

    public async Task<bool> RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && IsFresh())
            return false;

        var updated = await NasdaqSymbolListDownloader
            .UpdateAsync(_cacheDirectory, _http, ct)
            .ConfigureAwait(false);

        if (updated)
            Reload();

        return updated;
    }

    public void Reload()
    {
        if (string.IsNullOrWhiteSpace(_cacheDirectory))
            return;

        var nasdaqPath = Path.Combine(_cacheDirectory, NasdaqSymbolListDownloader.NasdaqListedFileName);
        var otherPath = Path.Combine(_cacheDirectory, NasdaqSymbolListDownloader.OtherListedFileName);

        var nasdaq = File.Exists(nasdaqPath)
            ? NasdaqSymbolListParser.ParseNasdaqListed(File.ReadAllText(nasdaqPath))
            : [];
        var other = File.Exists(otherPath)
            ? NasdaqSymbolListParser.ParseOtherListed(File.ReadAllText(otherPath))
            : [];

        _symbols = Merge(nasdaq, other);
    }

    private bool IsFresh()
    {
        var updatedAt = LastUpdatedAt;
        return updatedAt is not null &&
               DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime() < DefaultRefreshInterval &&
               File.Exists(Path.Combine(_cacheDirectory, NasdaqSymbolListDownloader.NasdaqListedFileName)) &&
               File.Exists(Path.Combine(_cacheDirectory, NasdaqSymbolListDownloader.OtherListedFileName));
    }

    private DateTimeOffset? ResolveLastUpdatedAt()
    {
        if (string.IsNullOrWhiteSpace(_cacheDirectory))
            return null;

        var files = new[]
        {
            Path.Combine(_cacheDirectory, NasdaqSymbolListDownloader.NasdaqListedFileName),
            Path.Combine(_cacheDirectory, NasdaqSymbolListDownloader.OtherListedFileName),
        };

        var existing = files
            .Where(File.Exists)
            .Select(path => new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero))
            .ToList();

        return existing.Count == 0 ? null : existing.Max();
    }

    private static IReadOnlyList<StockSearchResult> Merge(
        IEnumerable<StockSearchResult> nasdaq,
        IEnumerable<StockSearchResult> other)
    {
        return nasdaq
            .Concat(other)
            .GroupBy(s => (Symbol: s.Symbol.ToUpperInvariant(), Exchange: s.Exchange.ToUpperInvariant()))
            .Select(g => g.First())
            .OrderBy(s => s.Symbol)
            .ThenBy(s => ExchangeRank(s.Exchange))
            .ThenBy(s => s.Exchange)
            .ToList();
    }

    private static int ExchangeRank(string exchange) =>
        exchange.ToUpperInvariant() switch
        {
            "NASDAQ" => 0,
            "NYSE" => 1,
            "NYSEARCA" => 2,
            "AMEX" => 3,
            "BATS" => 4,
            "IEX" => 5,
            _ => 9,
        };
}
