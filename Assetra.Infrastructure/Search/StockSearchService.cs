using System.Text;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Search;

public sealed class StockSearchService : IStockSearchService
{
    private IReadOnlyList<StockSearchResult> _all;
    private Dictionary<string, string> _exchangeMap;
    private Dictionary<string, string> _nameMap;
    private Dictionary<string, string> _sectorMap;
    private HashSet<string> _etfSet;     // symbols whose type == "ETF"

    // twse_equities.csv  — UTF-8
    // tpex_equities.csv  — Big5 / cp950  (TPEX official export encoding)
    // .NET Core does not include legacy code pages by default; register the provider once.
    private static readonly Encoding Cp950 = GetCp950();

    private static Encoding GetCp950()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(950);
    }

    // Production constructor — loads CSV files from disk
    public StockSearchService(string assetsDirectory)
    {
        var twseCsv = File.ReadAllText(Path.Combine(assetsDirectory, "twse_equities.csv"), Encoding.UTF8);
        // Try UTF-8 first (new format from API download); fall back to Big5 (legacy)
        var tpexPath = Path.Combine(assetsDirectory, "tpex_equities.csv");
        var tpexCsv = ReadSmartEncoding(tpexPath);
        (_all, _exchangeMap, _nameMap, _sectorMap, _etfSet) = Parse(twseCsv, tpexCsv);
    }

    /// <summary>Reload data from disk (called after StockListDownloader updates CSVs).</summary>
    public void Reload(string assetsDirectory)
    {
        var twseCsv = File.ReadAllText(Path.Combine(assetsDirectory, "twse_equities.csv"), Encoding.UTF8);
        var tpexCsv = ReadSmartEncoding(Path.Combine(assetsDirectory, "tpex_equities.csv"));
        (_all, _exchangeMap, _nameMap, _sectorMap, _etfSet) = Parse(twseCsv, tpexCsv);
    }

    private static string ReadSmartEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var utf8 = Encoding.UTF8.GetString(bytes);
        // If UTF-8 decodes cleanly (no replacement chars), use it
        return utf8.Contains('\uFFFD') ? Cp950.GetString(bytes) : utf8;
    }

    // Test constructor — accepts CSV content directly
    internal StockSearchService(string twseCsvContent, string tpexCsvContent)
    {
        (_all, _exchangeMap, _nameMap, _sectorMap, _etfSet) = Parse(twseCsvContent, tpexCsvContent);
    }

    public IReadOnlyList<StockSearchResult> GetAll() => _all;

    public IReadOnlyList<StockSearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        query = query.Trim();
        return _all
            .Where(s => s.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();
    }

    public string? GetExchange(string symbol) => _exchangeMap.GetValueOrDefault(symbol);
    public string? GetName(string symbol) => _nameMap.GetValueOrDefault(symbol);
    public string? GetSector(string symbol) => _sectorMap.GetValueOrDefault(symbol);
    /// <summary>
    /// 是否為 ETF。CSV 原始分類優先，失敗時 fallback 到代碼規則 —
    /// 避免新上市或尚未被 CSV 分類的主動式/債券 ETF（如 00981A、00687B）被誤當成一般股票。
    /// </summary>
    public bool IsEtf(string symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return false;
        if (_etfSet.Contains(symbol)) return true;
        return IsEtfCodePattern(symbol);
    }

    /// <summary>
    /// 是否為**債券 ETF**（證交稅免徵至 2026 底）。
    /// Taiwan 慣例：代碼尾碼 'B' 且屬 ETF → 債券型（00687B、00725B、00751B…）。
    /// </summary>
    public bool IsBondEtf(string symbol)
    {
        if (!IsEtf(symbol)) return false;
        return symbol.EndsWith('B') || symbol.EndsWith('b');
    }

    /// <summary>
    /// 4–6 碼且以 "00" 開頭 → 台股 ETF 代碼規則（0050 / 00878 / 00981A / 00687B …）。
    /// 當 CSV 資料來源尚未把該代碼分類為 ETF 時作為保底判斷。
    /// </summary>
    private static bool IsEtfCodePattern(string symbol)
    {
        if (symbol.Length is < 4 or > 6) return false;
        return symbol.StartsWith("00", StringComparison.Ordinal);
    }

    private static (
        IReadOnlyList<StockSearchResult>,
        Dictionary<string, string>,
        Dictionary<string, string>,
        Dictionary<string, string>,
        HashSet<string>) Parse(string twseCsv, string tpexCsv)
    {
        var results = new List<StockSearchResult>();
        var exchangeMap = new Dictionary<string, string>();
        var nameMap = new Dictionary<string, string>();
        var sectorMap = new Dictionary<string, string>();
        var etfSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (csv, exchange) in new[] { (twseCsv, "TWSE"), (tpexCsv, "TPEX") })
        {
            foreach (var line in csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length < 3)
                    continue;
                var type = parts[0].Trim();
                if (type != "股票" && type != "ETF")
                    continue;
                var symbol = parts[1].Trim();
                var name = parts[2].Trim();
                if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(name))
                    continue;

                // CSV header: type,code,name,ISIN,start,market,group,CFI
                // parts[6] = group (sector), present only in TWSE equities CSV
                var sector = parts.Length > 6 ? parts[6].Trim() : string.Empty;
                var isEtf = type == "ETF";

                results.Add(new StockSearchResult(symbol, name, exchange, sector, isEtf));
                exchangeMap[symbol] = exchange;
                nameMap[symbol] = name;
                if (!string.IsNullOrEmpty(sector))
                    sectorMap[symbol] = sector;
                if (isEtf)
                    etfSet.Add(symbol);
            }
        }

        return (results, exchangeMap, nameMap, sectorMap, etfSet);
    }
}
