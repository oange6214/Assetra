using Assetra.Core.Models;

namespace Assetra.Infrastructure.Search;

internal static class NasdaqSymbolListParser
{
    public static IReadOnlyList<StockSearchResult> ParseNasdaqListed(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var rows = new List<StockSearchResult>();
        foreach (var fields in EnumeratePipeRows(content))
        {
            if (fields.Length < 8 || IsHeader(fields[0], "Symbol"))
                continue;

            var symbol = EquitySymbolNormalizer.NormalizeCanonicalSymbol(fields[0]);
            var name = fields[1].Trim();
            var isTestIssue = IsYes(fields[3]);
            if (symbol.Length == 0 || name.Length == 0 || isTestIssue)
                continue;

            rows.Add(new StockSearchResult(
                symbol,
                name,
                "NASDAQ",
                Sector: string.Empty,
                IsEtf: IsYes(fields[6]),
                Currency: "USD"));
        }

        return rows;
    }

    public static IReadOnlyList<StockSearchResult> ParseOtherListed(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var rows = new List<StockSearchResult>();
        foreach (var fields in EnumeratePipeRows(content))
        {
            if (fields.Length < 7 || IsHeader(fields[0], "ACT Symbol"))
                continue;

            var symbol = EquitySymbolNormalizer.NormalizeCanonicalSymbol(fields[0]);
            var name = fields[1].Trim();
            var exchange = MapOtherListedExchange(fields[2]);
            var isTestIssue = IsYes(fields[6]);
            if (symbol.Length == 0 || name.Length == 0 || exchange.Length == 0 || isTestIssue)
                continue;

            rows.Add(new StockSearchResult(
                symbol,
                name,
                exchange,
                Sector: string.Empty,
                IsEtf: IsYes(fields[4]),
                Currency: "USD"));
        }

        return rows;
    }

    private static IEnumerable<string[]> EnumeratePipeRows(string content)
    {
        foreach (var rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("File Creation Time:", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return line.Split('|');
        }
    }

    private static bool IsHeader(string value, string expected) =>
        string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsYes(string value) =>
        string.Equals(value.Trim(), "Y", StringComparison.OrdinalIgnoreCase);

    private static string MapOtherListedExchange(string value) =>
        value.Trim().ToUpperInvariant() switch
        {
            "A" or "M" => "AMEX",
            "N" => "NYSE",
            "P" => "NYSEARCA",
            "V" => "IEX",
            "Z" => "BATS",
            var code => code,
        };
}
