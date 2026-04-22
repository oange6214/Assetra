using Moq;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Settings;
using Assetra.WPF.Infrastructure;
using Wpf.Ui.Appearance;
using Xunit;

namespace Assetra.Tests.WPF;

public class SettingsViewModelTests
{
    private readonly Mock<IAppSettingsService> _mockSettings = new();
    private readonly Mock<IThemeService> _mockTheme = new();
    private readonly Mock<ILocalizationService> _mockLocalization = new();
    private readonly Mock<ICurrencyService> _mockCurrency = new();

    public SettingsViewModelTests()
    {
        _mockSettings.Setup(s => s.Current).Returns(new AppSettings());
        _mockSettings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        _mockTheme.Setup(t => t.CurrentTheme).Returns(ApplicationTheme.Dark);
        _mockLocalization.Setup(l => l.CurrentLanguage).Returns("zh-TW");
        _mockLocalization.Setup(l => l.Get(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string _, string fallback) => fallback);
        _mockCurrency.Setup(c => c.SupportedCurrencies)
            .Returns(["TWD", "USD", "JPY", "EUR", "HKD"]);
        _mockCurrency.Setup(c => c.Currency).Returns("TWD");
    }

    private SettingsViewModel CreateVm() =>
        new(_mockSettings.Object, _mockTheme.Object,
            _mockLocalization.Object, _mockCurrency.Object);

    [Fact]
    public void Constructor_DarkTheme_SetsIsDarkThemeTrue()
    {
        _mockTheme.Setup(t => t.CurrentTheme).Returns(ApplicationTheme.Dark);
        var vm = CreateVm();
        Assert.True(vm.IsDarkTheme);
    }

    [Fact]
    public void Constructor_LightTheme_SetsIsDarkThemeFalse()
    {
        _mockTheme.Setup(t => t.CurrentTheme).Returns(ApplicationTheme.Light);
        var vm = CreateVm();
        Assert.False(vm.IsDarkTheme);
    }

    [Fact]
    public void Constructor_LoadsLanguage_FromSettings()
    {
        _mockSettings.Setup(s => s.Current)
                     .Returns(new AppSettings(Language: "en-US"));
        var vm = CreateVm();
        Assert.Equal("en-US", vm.Language);
    }

    [Fact]
    public void Constructor_DefaultSettings_LoadsTaiwanColors()
    {
        var vm = CreateVm();
        Assert.True(vm.UseTaiwanColors);
        Assert.False(vm.UseInternationalColors);
    }

    [Fact]
    public void Constructor_FalseColorScheme_SetsUseInternationalColors()
    {
        _mockSettings.Setup(s => s.Current)
                     .Returns(new AppSettings(TaiwanColorScheme: false));
        var vm = CreateVm();
        Assert.False(vm.UseTaiwanColors);
        Assert.True(vm.UseInternationalColors);
    }

    [Fact]
    public void Constructor_PopulatesSupportedCurrencies()
    {
        var vm = CreateVm();
        Assert.Contains("TWD", vm.SupportedCurrencies);
        Assert.Contains("USD", vm.SupportedCurrencies);
    }

    [Fact]
    public async Task SaveDataSourceSettingsCommand_PersistsQuoteProviderHistoryProviderAndFugleKey()
    {
        var vm = CreateVm();

        vm.QuoteProvider = "fugle";
        vm.HistoryProvider = "fugle";
        vm.FugleApiKey = "demo-key";
        await vm.SaveDataSourceSettingsCommand.ExecuteAsync(null);

        _mockSettings.Verify(s => s.SaveAsync(It.Is<AppSettings>(settings =>
            settings.QuoteProvider == "fugle"
            && settings.HistoryProvider == "fugle"
            && settings.FugleApiKey == "demo-key")), Times.AtLeastOnce);
    }
}
