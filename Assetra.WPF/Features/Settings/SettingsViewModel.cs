using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Assetra.Core.Interfaces;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;

namespace Assetra.WPF.Features.Settings;

public sealed record LanguageOption(string Code, string Display);
public sealed record ProviderOption(string Code, string Display);

/// <summary>
/// Minimal settings surface for Assetra.
///
/// Trimmed from Stockra's large SettingsViewModel (LLM providers, Jin10 flash,
/// FinMind token, screener presets, strategy library etc. are all removed).
///
/// Kept:
///   • Theme switcher (Light / Dark / System) via IThemeService
///   • Language switcher (zh-TW / en-US) via ILocalizationService
///   • Primary currency dropdown via ICurrencyService
///   • Colour-scheme toggle (Taiwan red-up / International green-up)
///     — persisted to AppSettings.TaiwanColorScheme
///
/// Note: CommissionDiscount and TaiwanStyleFees from the Phase 5 spec are
/// not yet fields on AppSettings, so the UI omits them. Per-trade discount
/// is already entered inline in the PortfolioView transaction dialog.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("zh-TW", "正體中文"),
        new("en-US", "English"),
    ];

    public static IReadOnlyList<ApplicationTheme> Themes { get; } =
    [
        ApplicationTheme.Light,
        ApplicationTheme.Dark,
    ];

    public static IReadOnlyList<ProviderOption> SupportedQuoteProviders { get; } =
    [
        new("official", "TWSE / TPEX"),
        new("fugle", "Fugle"),
    ];

    public static IReadOnlyList<ProviderOption> SupportedHistoryProviders { get; } =
    [
        new("twse", "TWSE / TPEX"),
        new("fugle", "Fugle"),
        new("yahoo", "Yahoo Finance"),
        new("finmind", "FinMind"),
    ];

    private readonly IAppSettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;
    private readonly ICurrencyService _currencyService;
    private readonly ISnackbarService? _snackbar;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SyncSettingsViewModel Sync { get; }
    public ConflictResolutionViewModel Conflicts { get; }

    private bool _isLoading;
    private int _saveVersion;

    [ObservableProperty] private string _language = "zh-TW";
    [ObservableProperty] private bool _useTaiwanColors = true;

    /// <summary>Mirror property for the "國際 / International" RadioButton — inverse of <see cref="UseTaiwanColors"/>.</summary>
    public bool UseInternationalColors
    {
        get => !UseTaiwanColors;
        set { if (value) UseTaiwanColors = false; }
    }

    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private double _uiScale = 1.0;
    [ObservableProperty] private string _primaryCurrency = "TWD";
    [ObservableProperty] private string _baseCurrency = "TWD";
    [ObservableProperty] private string _quoteProvider = "official";
    [ObservableProperty] private string _historyProvider = "twse";
    [ObservableProperty] private string _fugleApiKey = string.Empty;
    [ObservableProperty] private string _dataSourceSaveStatus = string.Empty;
    [ObservableProperty] private bool _isFugleHelpOpen;
    [ObservableProperty] private string _ocrTessdataPath = string.Empty;
    [ObservableProperty] private string _ocrLanguage = "eng";

    public ObservableCollection<string> SupportedCurrencies { get; } = [];

    public string AppVersion { get; } = ResolveAppVersion();
    public bool IsFugleConfigured => !string.IsNullOrWhiteSpace(FugleApiKey);

    private double _uiScalePreview = 1.0;

    public double UiScalePreview
    {
        get => _uiScalePreview;
        set
        {
            var snapped = Math.Clamp(Math.Round(value / 0.05) * 0.05, 0.75, 1.5);
            if (Math.Abs(_uiScalePreview - snapped) < 0.001)
                return;

            _uiScalePreview = snapped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UiScaleDisplay));
        }
    }

    public string UiScaleDisplay => $"{_uiScalePreview:0.00}×";

    public SettingsViewModel(
        IAppSettingsService settings,
        IThemeService theme,
        ILocalizationService localization,
        ICurrencyService currencyService,
        SyncSettingsViewModel sync,
        ConflictResolutionViewModel conflicts,
        ISnackbarService? snackbar = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(currencyService);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(conflicts);

        _settings = settings;
        _theme = theme;
        _localization = localization;
        _currencyService = currencyService;
        _snackbar = snackbar;
        Sync = sync;
        Conflicts = conflicts;

        foreach (var code in _currencyService.SupportedCurrencies)
            SupportedCurrencies.Add(code);

        LoadFromSettings();

        _theme.ThemeChanged += OnThemeChanged;
    }

    private void LoadFromSettings()
    {
        _isLoading = true;
        try
        {
            var s = _settings.Current;
            Language = s.Language;
            UseTaiwanColors = s.TaiwanColorScheme;
            IsDarkTheme = _theme.CurrentTheme == ApplicationTheme.Dark;
            UiScale = s.UiScale is >= 0.75 and <= 1.5 ? s.UiScale : 1.0;
            PrimaryCurrency = string.IsNullOrWhiteSpace(s.PreferredCurrency)
                ? "TWD"
                : s.PreferredCurrency;
            BaseCurrency = string.IsNullOrWhiteSpace(s.BaseCurrency) ? "TWD" : s.BaseCurrency;
            QuoteProvider = string.IsNullOrWhiteSpace(s.QuoteProvider) ? "official" : s.QuoteProvider;
            HistoryProvider = string.IsNullOrWhiteSpace(s.HistoryProvider) ? "twse" : s.HistoryProvider;
            FugleApiKey = s.FugleApiKey ?? string.Empty;
            OcrTessdataPath = s.OcrTessdataPath ?? string.Empty;
            OcrLanguage = string.IsNullOrWhiteSpace(s.OcrLanguage) ? "eng" : s.OcrLanguage;
            DataSourceSaveStatus = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnLanguageChanged(string value)
    {
        if (_isLoading)
            return;
        _localization.SetLanguage(value);
        _ = SaveAsync();
    }

    partial void OnUseTaiwanColorsChanged(bool value)
    {
        OnPropertyChanged(nameof(UseInternationalColors));
        if (_isLoading)
            return;
        ColorSchemeService.Apply(value, _theme.CurrentTheme);
        _ = SaveAsync();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (_isLoading)
            return;
        _theme.Apply(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    partial void OnPrimaryCurrencyChanged(string value)
    {
        if (_isLoading)
            return;
        _ = ApplyPrimaryCurrencyAsync(value);
    }

    partial void OnBaseCurrencyChanged(string value)
    {
        if (_isLoading)
            return;
        _ = SaveAsync();
    }

    partial void OnUiScaleChanged(double value)
    {
        _uiScalePreview = value;
        OnPropertyChanged(nameof(UiScalePreview));
        OnPropertyChanged(nameof(UiScaleDisplay));
    }

    partial void OnQuoteProviderChanged(string value)
    {
        if (_isLoading)
            return;
        DataSourceSaveStatus = string.Empty;
        _ = SaveAsync();
    }

    partial void OnHistoryProviderChanged(string value)
    {
        if (_isLoading)
            return;
        DataSourceSaveStatus = string.Empty;
        _ = SaveAsync();
    }

    partial void OnFugleApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsFugleConfigured));
        if (_isLoading)
            return;
        DataSourceSaveStatus = string.Empty;
    }

    partial void OnOcrTessdataPathChanged(string value)
    {
        if (_isLoading)
            return;
        _ = SaveAsync();
    }

    partial void OnOcrLanguageChanged(string value)
    {
        if (_isLoading)
            return;
        _ = SaveAsync();
    }

    [RelayCommand]
    private void BrowseOcrTessdata()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select tessdata folder",
            InitialDirectory = Directory.Exists(OcrTessdataPath) ? OcrTessdataPath : string.Empty,
        };
        if (dialog.ShowDialog() == true)
            OcrTessdataPath = dialog.FolderName;
    }

    private void OnThemeChanged(ApplicationTheme theme)
    {
        _isLoading = true;
        try
        { IsDarkTheme = theme == ApplicationTheme.Dark; }
        finally { _isLoading = false; }

        // Re-apply colour scheme so up/down brushes track the new theme
        ColorSchemeService.Apply(UseTaiwanColors, theme);
    }

    private Task<bool> SaveAsync(bool showDataSourceSuccess = false)
    {
        var version = Interlocked.Increment(ref _saveVersion);
        return SaveLatestAsync(version, showDataSourceSuccess);
    }

    private async Task<bool> SaveLatestAsync(int version, bool showDataSourceSuccess)
    {
        var enteredGate = false;
        try
        {
            await _saveGate.WaitAsync().ConfigureAwait(true);
            enteredGate = true;

            if (version != Volatile.Read(ref _saveVersion))
                return true;

            await SaveSnapshotAsync().ConfigureAwait(true);
            if (showDataSourceSuccess)
                DataSourceSaveStatus = _localization.Get("Settings.DataSource.Saved");
            return true;
        }
        catch (Exception ex)
        {
            ShowSaveFailure(ex);
            return false;
        }
        finally
        {
            if (enteredGate)
                _saveGate.Release();
        }
    }

    private async Task ApplyPrimaryCurrencyAsync(string value)
    {
        try
        {
            await _currencyService.ApplyAsync(value).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowSaveFailure(ex);
        }
    }

    private async Task SaveSnapshotAsync()
    {
        var updated = _settings.Current with
        {
            Language = Language,
            TaiwanColorScheme = UseTaiwanColors,
            UiScale = UiScale,
            PreferredCurrency = PrimaryCurrency,
            BaseCurrency = BaseCurrency,
            QuoteProvider = QuoteProvider,
            HistoryProvider = HistoryProvider,
            FugleApiKey = FugleApiKey.Trim(),
            OcrTessdataPath = OcrTessdataPath?.Trim() ?? string.Empty,
            OcrLanguage = string.IsNullOrWhiteSpace(OcrLanguage) ? "eng" : OcrLanguage.Trim(),
        };
        await _settings.SaveAsync(updated).ConfigureAwait(true);
    }

    private void ShowSaveFailure(Exception ex)
    {
        var message = $"{_localization.Get("Settings.SaveFailed", "Settings could not be saved.")} {ex.Message}";
        DataSourceSaveStatus = message;
        _snackbar?.Error(message);
    }

    public async Task CommitUiScaleAsync()
    {
        if (Math.Abs(UiScale - _uiScalePreview) < 0.001)
            return;

        UiScale = _uiScalePreview;
        if (!_isLoading)
            await SaveAsync();
    }

    [RelayCommand]
    private async Task SaveDataSourceSettingsAsync()
    {
        await SaveAsync(showDataSourceSuccess: true);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Assetra");
        if (Directory.Exists(folder))
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFugleHelp() => IsFugleHelpOpen = true;

    [RelayCommand]
    private void CloseFugleHelp() => IsFugleHelpOpen = false;

    [RelayCommand]
    private void OpenFugleDeveloperSite()
    {
        Process.Start(new ProcessStartInfo("https://developer.fugle.tw/") { UseShellExecute = true });
    }

    public string DataFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Assetra");

    private static string ResolveAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?
            .Trim();
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.StartsWith('v') ? informational : $"v{informational}";

        var version = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "v0.0.0" : $"v{version}";
    }

    public void Dispose()
    {
        _theme.ThemeChanged -= OnThemeChanged;
    }
}
