using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.MarketData;
using Xunit;

namespace Assetra.Tests.Infrastructure.MarketData;

public class TwelveDataQuotaTrackerTests
{
    // Records every SaveAsync call (the saved settings + raiseChanged flag) and tracks
    // whether the public Changed event was ever raised — so a test can assert that
    // bookkeeping persistence is silent.
    private sealed class RecordingSettings : IAppSettingsService
    {
        public AppSettings Current { get; private set; }
        public List<(AppSettings Settings, bool RaiseChanged)> Saves { get; } = new();
        public int ChangedInvocations { get; private set; }

        public RecordingSettings(AppSettings initial)
        {
            Current = initial;
            // Subscribe so we can detect any (erroneous) Changed fired during bookkeeping.
            Changed += () => ChangedInvocations++;
        }

        public event Action? Changed;

        public Task SaveAsync(AppSettings settings, bool raiseChanged = true)
        {
            Current = settings;
            Saves.Add((settings, raiseChanged));
            if (raiseChanged)
                Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RecordAsync_PersistsQuotaUsage_WithoutRaisingChanged()
    {
        // WHY: quota bookkeeping persists usage on every TwelveData fetch. If that
        // SaveAsync raised the global settings-Changed event, every Changed subscriber
        // (Portfolio/FinancialOverview/…) would re-run, and the Portfolio handler would
        // RefreshNow → fetch → record → Save → Changed → … an infinite feedback loop
        // running several times a second. Recording usage must therefore be SILENT:
        // it must update Current and persist, but never fire Changed.
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var settings = new RecordingSettings(
            new AppSettings(TwelveDataQuotaDate: today, TwelveDataQuotaUsed: 5));
        var tracker = new TwelveDataQuotaTracker(settings);

        await tracker.RecordAsync(3);

        // Persisted exactly once, and explicitly with raiseChanged == false.
        var save = Assert.Single(settings.Saves);
        Assert.False(save.RaiseChanged);

        // Current was updated and the increment is reflected in UsedToday.
        Assert.Equal(today, settings.Current.TwelveDataQuotaDate);
        Assert.Equal(8, settings.Current.TwelveDataQuotaUsed);
        Assert.Equal(8, tracker.UsedToday);

        // The loop-breaking guarantee: bookkeeping never fired the global Changed event.
        Assert.Equal(0, settings.ChangedInvocations);
    }

    [Fact]
    public async Task RecordAsync_NewUtcDay_ResetsThenAddsCredits_StillSilent()
    {
        // WHY: when the stored quota date is stale (yesterday), usage resets to 0 before
        // adding today's credits. This must still persist silently — same loop concern.
        var settings = new RecordingSettings(
            new AppSettings(TwelveDataQuotaDate: "2000-01-01", TwelveDataQuotaUsed: 42));
        var tracker = new TwelveDataQuotaTracker(settings);

        await tracker.RecordAsync(2);

        var save = Assert.Single(settings.Saves);
        Assert.False(save.RaiseChanged);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), settings.Current.TwelveDataQuotaDate);
        Assert.Equal(2, settings.Current.TwelveDataQuotaUsed);
        Assert.Equal(0, settings.ChangedInvocations);
    }
}
