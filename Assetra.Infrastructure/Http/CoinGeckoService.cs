using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Interfaces;

namespace Assetra.Infrastructure.Http;

/// <summary>
/// Fetches crypto prices from the CoinGecko free public API (no API key required).
/// Rate limit: ~30 req/min on the free tier.
/// </summary>
public sealed class CoinGeckoService : ICryptoService, IDisposable
{
    private static readonly Dictionary<string, string> SymbolToId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "bitcoin",
        ["ETH"] = "ethereum",
        ["SOL"] = "solana",
        ["BNB"] = "binancecoin",
        ["USDT"] = "tether",
        ["USDC"] = "usd-coin",
        ["XRP"] = "ripple",
        ["ADA"] = "cardano",
        ["DOGE"] = "dogecoin",
        ["DOT"] = "polkadot",
        ["AVAX"] = "avalanche-2",
        ["MATIC"] = "matic-network",
        ["POL"] = "matic-network",
        ["LINK"] = "chainlink",
        ["UNI"] = "uniswap",
        ["ATOM"] = "cosmos",
        ["LTC"] = "litecoin",
        ["BCH"] = "bitcoin-cash",
        ["NEAR"] = "near",
        ["APT"] = "aptos",
        ["SUI"] = "sui",
        ["TON"] = "the-open-network",
        ["TRX"] = "tron",
        ["OP"] = "optimism",
        ["ARB"] = "arbitrum",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public CoinGeckoService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.coingecko.com"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesTwdAsync(IReadOnlyList<string> symbols)
    {
        if (symbols.Count == 0)
            return new Dictionary<string, decimal>();

        // Build id list from known symbols; skip unknown
        var pairs = symbols
            .Where(s => SymbolToId.ContainsKey(s))
            .Select(s => (Symbol: s.ToUpperInvariant(), Id: SymbolToId[s]))
            .DistinctBy(p => p.Id)
            .ToList();

        if (pairs.Count == 0)
            return new Dictionary<string, decimal>();

        var ids = string.Join(",", pairs.Select(p => p.Id));
        var url = $"/api/v3/simple/price?ids={ids}&vs_currencies=twd";

        try
        {
            var json = await _http.GetFromJsonAsync<Dictionary<string, CoinPrice>>(url, JsonOptions)
                       ?? new Dictionary<string, CoinPrice>();

            // Build reverse map: coinGeckoId → symbol(s)
            var idToSymbols = new Dictionary<string, List<string>>();
            foreach (var (sym, id) in pairs)
            {
                if (!idToSymbols.TryGetValue(id, out var list))
                {
                    list = [];
                    idToSymbols[id] = list;
                }
                list.Add(sym);
            }

            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, price) in json)
            {
                if (price.Twd is not { } twd)
                    continue;
                if (!idToSymbols.TryGetValue(id, out var syms))
                    continue;
                foreach (var sym in syms)
                    result[sym] = twd;
            }
            return result;
        }
        catch
        {
            // Network error / rate limit — return empty; callers handle gracefully
            return new Dictionary<string, decimal>();
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed class CoinPrice
    {
        [JsonPropertyName("twd")]
        public decimal? Twd { get; set; }
    }
}
