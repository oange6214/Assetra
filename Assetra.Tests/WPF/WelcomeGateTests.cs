using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Shell;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class WelcomeGateTests
{
    [Fact]
    public void ShowWelcome_TrueWhenFlagUnset()
    {
        var settings = new FakeSettings(new AppSettings(HasShownWelcomeBanner: false));
        var vm = new WelcomeGateViewModel(settings, _ => { });

        Assert.True(vm.ShowWelcome);
    }

    [Fact]
    public void ShowWelcome_FalseWhenFlagAlreadySet()
    {
        var settings = new FakeSettings(new AppSettings(HasShownWelcomeBanner: true));
        var vm = new WelcomeGateViewModel(settings, _ => { });

        Assert.False(vm.ShowWelcome);
    }

    [Fact]
    public void Dismiss_HidesOverlayAndPersistsFlag()
    {
        var settings = new FakeSettings(new AppSettings(HasShownWelcomeBanner: false));
        var vm = new WelcomeGateViewModel(settings, _ => { });

        vm.DismissWelcomeCommand.Execute(null);

        Assert.False(vm.ShowWelcome);
        Assert.True(settings.Current.HasShownWelcomeBanner);
    }

    [Fact]
    public void Dismiss_DoesNotRaiseChanged()
    {
        // The seen-flag is bookkeeping — persisting it must NOT fire the app-wide
        // Changed reload (same landmine the nav-pref task avoided).
        var settings = new FakeSettings(new AppSettings(HasShownWelcomeBanner: false));
        var changedRaised = false;
        settings.Changed += () => changedRaised = true;
        var vm = new WelcomeGateViewModel(settings, _ => { });

        vm.DismissWelcomeCommand.Execute(null);

        Assert.False(changedRaised);
    }

    [Fact]
    public void StartAddFirstHolding_SetsFlagAndNavigatesToPortfolio()
    {
        var settings = new FakeSettings(new AppSettings(HasShownWelcomeBanner: false));
        NavSection? navigatedTo = null;
        var vm = new WelcomeGateViewModel(settings, section => navigatedTo = section);

        vm.StartAddFirstHoldingCommand.Execute(null);

        Assert.False(vm.ShowWelcome);
        Assert.True(settings.Current.HasShownWelcomeBanner);
        Assert.Equal(NavSection.Portfolio, navigatedTo);
    }

    /// <summary>
    /// Minimal in-file fake mirroring the real service (same shape as the one in
    /// <see cref="NavRailViewModelTests"/>): <see cref="Current"/> holds the seeded
    /// record, <see cref="SaveAsync"/> applies the mutation in place and honours
    /// <paramref name="raiseChanged"/> so bookkeeping saves (false) skip Changed.
    /// </summary>
    private sealed class FakeSettings(AppSettings initial) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = initial;
        public event Action? Changed;

        public Task SaveAsync(AppSettings settings, bool raiseChanged = true)
        {
            Current = settings;
            if (raiseChanged)
                Changed?.Invoke();
            return Task.CompletedTask;
        }
    }
}
