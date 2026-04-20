using System.Reactive.Concurrency;
using Moq;
using Assetra.Core.Interfaces;
using Assetra.WPF.Features.StatusBar;
using Xunit;

namespace Assetra.Tests.WPF;

public class StatusBarViewModelTests
{
    private static readonly Mock<ILocalizationService> MockLocalization = new();

    static StatusBarViewModelTests()
    {
        MockLocalization.Setup(l => l.Get(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string _, string fallback) => fallback);
    }

    private static StatusBarViewModel CreateVm() =>
        new(TaskPoolScheduler.Default, MockLocalization.Object);

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
}
