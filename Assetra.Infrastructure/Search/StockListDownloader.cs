using System.Text;
using System.Text.Json;

namespace Assetra.Infrastructure.Search;

/// <summary>
/// Downloads stock lists from TWSE/TPEX official OpenAPIs and caches as UTF-8 CSV.
/// APIs return JSON with Big5-encoded names; this class decodes and normalizes to UTF-8.
/// </summary>
public static class StockListDownloader
{
    private static readonly Encoding Cp950;

    static StockListDownloader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp950 = Encoding.GetEncoding(950);
    }

    private const string TwseUrl = "https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL";
    private const string TpexUrl = "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes";

    /// <summary>
    /// Downloads fresh stock lists from TWSE + TPEX and writes UTF-8 CSV files.
    /// Returns true if at least one file was updated.
    /// </summary>
    public static async Task<bool> UpdateAsync(string assetsDirectory, CancellationToken ct = default)
    {
        using var http = CreateClient();
        var updated = false;

        var twseTask = DownloadTwseAsync(http, ct);
        var tpexTask = DownloadTpexAsync(http, ct);

        try
        {
            var twseRows = await twseTask;
            if (twseRows.Count > 0)
            {
                WriteCsv(Path.Combine(assetsDirectory, "twse_equities.csv"), twseRows);
                updated = true;
            }
        }
        catch { /* keep existing file on failure */ }

        try
        {
            var tpexRows = await tpexTask;
            if (tpexRows.Count > 0)
            {
                WriteCsv(Path.Combine(assetsDirectory, "tpex_equities.csv"), tpexRows);
                updated = true;
            }
        }
        catch { /* keep existing file on failure */ }

        return updated;
    }

    private static async Task<List<string[]>> DownloadTwseAsync(HttpClient http, CancellationToken ct)
    {
        var bytes = await http.GetByteArrayAsync(TwseUrl, ct);
        var json = DecodeSmart(bytes);
        using var doc = JsonDocument.Parse(json);
        var rows = new List<string[]>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var code = item.GetProperty("Code").GetString()?.Trim() ?? "";
            var name = item.GetProperty("Name").GetString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
                continue;

            var type = IsEtfCode(code) ? "ETF" : "股票";
            rows.Add([type, code, name, "", "", "TWSE", "", ""]);
        }

        return rows;
    }

    private static async Task<List<string[]>> DownloadTpexAsync(HttpClient http, CancellationToken ct)
    {
        var bytes = await http.GetByteArrayAsync(TpexUrl, ct);
        var json = DecodeSmart(bytes);
        using var doc = JsonDocument.Parse(json);
        var rows = new List<string[]>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var code = item.GetProperty("SecuritiesCompanyCode").GetString()?.Trim() ?? "";
            var name = item.GetProperty("CompanyName").GetString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
                continue;

            var type = IsEtfCode(code) ? "ETF" : "股票";
            rows.Add([type, code, name, "", "", "TPEX", "", ""]);
        }

        return rows;
    }

    /// <summary>
    /// TWSE/TPEX APIs sometimes return Big5-encoded bytes in a JSON wrapper.
    /// Try UTF-8 first; if garbled (contains replacement chars), fall back to Big5.
    /// </summary>
    private static string DecodeSmart(byte[] bytes)
    {
        var utf8 = Encoding.UTF8.GetString(bytes);
        // If UTF-8 decode produced no replacement characters, it's valid UTF-8
        if (!utf8.Contains('\uFFFD'))
            return utf8;

        // Fall back to Big5 (cp950)
        return Cp950.GetString(bytes);
    }

    private static bool IsEtfCode(string code)
    {
        // Taiwan ETF codes: 0050-style (4+ digits starting with 00) or 6-char codes
        if (code.Length >= 5 && code.StartsWith("00"))
            return true;
        if (code.Length == 6 && code[..2] == "00")
            return true;
        return false;
    }

    private static void WriteCsv(string path, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("type,code,name,ISIN,start,market,group,CFI");
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', row));
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler();
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Stockra/1.0");
        return client;
    }
}
