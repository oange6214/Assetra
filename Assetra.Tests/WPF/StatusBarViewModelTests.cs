using Assetra.Core.Interfaces;
using Assetra.WPF.Features.StatusBar;
using Microsoft.Reactive.Testing;
using Xunit;

namespace Assetra.Tests.WPF;

public class StatusBarViewModelTests
{
    private static StatusBarViewModel CreateVm() =>
        new(new TestScheduler(), new FakeLocalizationService());

    [Fact]
    public void Constructor_InitializesClockText()
    {
        var vm = CreateVm();
        // ClockText should be in HH:mm:ss format (non-empty)
        Assert.NotEmpty(vm.ClockText);
        Assert.Equal(8, vm.ClockText.Length); // "HH:mm:ss" is 8 chars
    }

    [Fact]
    public void Constructor_InitializesMarketStatusText()
    {
        var vm = CreateVm();
        // Should be either open or closed status text (non-null)
        Assert.NotNull(vm.MarketStatusText);
    }

    [Fact]
    public void IsMarketOpen_PropertyIsAccessible()
    {
        var vm = CreateVm();
        // Property must be accessible (bool) without throwing
        _ = vm.IsMarketOpen;
        Assert.True(true);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var vm = CreateVm();
        var ex = Record.Exception(() => vm.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_UnsubscribesFromLanguageChanged()
    {
        var localization = new FakeLocalizationService();
        var vm = new StatusBarViewModel(new TestScheduler(), localization);
        var before = localization.GetCallCount;

        vm.Dispose();
        localization.SetLanguage("en-US");

        Assert.Equal(before, localization.GetCallCount);
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public string CurrentLanguage { get; private set; } = "zh-TW";
        public int GetCallCount { get; private set; }

        public event EventHandler? LanguageChanged;

        public string Get(string key, string fallback = "")
        {
            GetCallCount++;
            return fallback;
        }

        public void SetLanguage(string languageCode)
        {
            CurrentLanguage = languageCode;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
