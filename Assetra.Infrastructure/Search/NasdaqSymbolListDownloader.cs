namespace Assetra.Infrastructure.Search;

internal static class NasdaqSymbolListDownloader
{
    private const string NasdaqListedUrl = "https://www.nasdaqtrader.com/dynamic/symdir/nasdaqlisted.txt";
    private const string OtherListedUrl = "https://www.nasdaqtrader.com/dynamic/symdir/otherlisted.txt";

    public const string NasdaqListedFileName = "nasdaqlisted.txt";
    public const string OtherListedFileName = "otherlisted.txt";

    public static async Task<bool> UpdateAsync(
        string cacheDirectory,
        HttpClient http,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(http);

        Directory.CreateDirectory(cacheDirectory);

        var updated = false;
        updated |= await TryDownloadAsync(
            http,
            NasdaqListedUrl,
            Path.Combine(cacheDirectory, NasdaqListedFileName),
            ct).ConfigureAwait(false);
        updated |= await TryDownloadAsync(
            http,
            OtherListedUrl,
            Path.Combine(cacheDirectory, OtherListedFileName),
            ct).ConfigureAwait(false);

        return updated;
    }

    private static async Task<bool> TryDownloadAsync(
        HttpClient http,
        string url,
        string targetPath,
        CancellationToken ct)
    {
        try
        {
            var content = await http.GetStringAsync(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content) || !content.Contains('|'))
                return false;

            var tempPath = $"{targetPath}.tmp";
            await File.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
            File.Move(tempPath, targetPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
