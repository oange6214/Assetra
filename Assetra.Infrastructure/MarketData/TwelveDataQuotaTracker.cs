using Assetra.Core.Interfaces;

namespace Assetra.Infrastructure.MarketData;

internal sealed class TwelveDataQuotaTracker(IAppSettingsService settings) : ITwelveDataQuotaTracker
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int DailyLimit => Math.Max(1, settings.Current.TwelveDataDailyQuota);

    public int UsedToday
    {
        get
        {
            var today = TodayKey();
            return string.Equals(settings.Current.TwelveDataQuotaDate, today, StringComparison.Ordinal)
                ? Math.Max(0, settings.Current.TwelveDataQuotaUsed)
                : 0;
        }
    }

    public string UsageDate => TodayKey();

    public async Task RecordAsync(int credits, CancellationToken ct = default)
    {
        if (credits <= 0)
            return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var today = TodayKey();
            var current = settings.Current;
            var used = string.Equals(current.TwelveDataQuotaDate, today, StringComparison.Ordinal)
                ? Math.Max(0, current.TwelveDataQuotaUsed)
                : 0;

            // 純記帳：靜默存檔（raiseChanged:false），避免觸發全域 Changed 而引發
            // 報價刷新 → 再記帳 → 再觸發的回饋迴圈。
            await settings.SaveAsync(current with
            {
                TwelveDataQuotaDate = today,
                TwelveDataQuotaUsed = used + credits,
            }, raiseChanged: false).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string TodayKey() => DateTime.UtcNow.ToString("yyyy-MM-dd");
}
