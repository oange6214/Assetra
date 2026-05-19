using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Assetra.Core.Interfaces;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Settings;

public sealed record LanguageOption(string Code, string Display);
public sealed record ProviderOption(string Code, string Display);

/// <summary>
/// 顯示用：1 單位外幣 = N 台幣。<see cref="RateDisplay"/> 已格式化為 4 位小數。
/// </summary>
public sealed record FxRateRow(string Currency, decimal Rate)
{
    public string RateDisplay => $"1 {Currency} = {Rate:N4} TWD";
}

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
        new("official", "TWSE / TPEX 官方"),
        new("fugle", "Fugle 台股"),
    ];

    /// <summary>
    /// Pages eligible to be the user's default startup landing.
    /// Code matches NavSection enum names so it round-trips into AppSettings
    /// without an extra mapping. Display strings are loaded at first use
    /// from the active language ResourceDictionary.
    /// </summary>
    public static IReadOnlyList<ProviderOption> SupportedHomeSections { get; } =
    [
        new("FinancialOverview", "財務總覽"),
        new("Portfolio", "投資組合"),
        new("Trends", "資產趨勢"),
        new("Reports", "月結報告"),
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
    private readonly IRefreshableSymbolDirectory? _usSymbolDirectory;
    private readonly ITwelveDataConnectionTester? _twelveDataTester;
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
    [ObservableProperty] private string _twelveDataApiKey = string.Empty;
    [ObservableProperty] private string _twelveDataTestStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTestTwelveDataConnection))]
    private bool _isTestingTwelveDataConnection;

    [ObservableProperty] private string _dataSourceSaveStatus = string.Empty;
    [ObservableProperty] private string _usSymbolDirectoryStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefreshUsSymbolDirectory))]
    private bool _isRefreshingUsSymbolDirectory;

    // P4.1d — FX *history* refresh (distinct from the live FxRate refresh handled below)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastFxHistoryRefreshDisplay))]
    private DateTimeOffset? _lastFxHistoryRefreshAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefreshFxHistory))]
    private bool _isRefreshingFxHistory;

    public string LastFxHistoryRefreshDisplay =>
        LastFxHistoryRefreshAt is null
            ? _localization?.Get("Settings.FxHistory.NeverRefreshed", "尚未更新過") ?? "尚未更新過"
            : string.Format(
                _localization?.Get("Settings.FxHistory.LastRefreshedFormat", "上次更新：{0:yyyy-MM-dd HH:mm}") ?? "上次更新：{0:yyyy-MM-dd HH:mm}",
                LastFxHistoryRefreshAt.Value.LocalDateTime);

    public bool CanRefreshFxHistory => !IsRefreshingFxHistory && _fxRefresher is not null;

    [ObservableProperty] private bool _isFugleHelpOpen;
    [ObservableProperty] private bool _isTwelveDataHelpOpen;
    [ObservableProperty] private string _ocrTessdataPath = string.Empty;
    [ObservableProperty] private string _ocrLanguage = "eng";
    [ObservableProperty] private string _defaultHomeSection = "FinancialOverview";

    // ── AMT 最低稅負制設定（v2 — 免稅額/稅率改由 TaxYearProfile 動態提供）────
    // 此處保留「使用者年度報稅彙整輸入」共 7 項。
    [ObservableProperty] private decimal _amtRegularTaxableIncome = 0m;
    [ObservableProperty] private decimal _amtRegularIncomeTax = 0m;
    [ObservableProperty] private decimal _amtInsuranceDeathProceeds = 0m;
    [ObservableProperty] private decimal _amtInsuranceNonDeathProceeds = 0m;
    [ObservableProperty] private decimal _amtUnlistedSecurityGains = 0m;
    [ObservableProperty] private decimal _amtNonCashDonation = 0m;
    [ObservableProperty] private decimal _amtPrivateFundGains = 0m;
    [ObservableProperty] private decimal _amtOverseasTaxCredit = 0m;

    // ── 個人稅務檔案 ──────────────────────────────────────────────────
    [ObservableProperty] private bool _taxIsMarried;
    [ObservableProperty] private int _taxDependentCount;
    [ObservableProperty] private int _taxPreschoolCount;
    [ObservableProperty] private int _taxCollegeStudentCount;
    [ObservableProperty] private int _taxLongCareCount;
    [ObservableProperty] private int _taxDisabilityCount;
    [ObservableProperty] private decimal _taxSalaryIncome;
    [ObservableProperty] private decimal _taxInterestIncome;
    [ObservableProperty] private decimal _taxRentalExpense;
    [ObservableProperty] private bool _taxUseItemizedDeduction;
    [ObservableProperty] private decimal _taxItemizedDeductionAmount;
    [ObservableProperty] private bool _taxDividendSeparate;

    // ── AI 助手 LLM provider 設定 ─────────────────────────────────
    // 空字串 / "null" → NullLlmProvider（rule-based only）
    // "openai" → 需要 LlmApiKey
    // "ollama" → 預設 endpoint http://localhost:11434

    [ObservableProperty] private string _llmProvider = string.Empty;
    [ObservableProperty] private string _llmApiKey = string.Empty;
    [ObservableProperty] private string _llmModel = string.Empty;
    [ObservableProperty] private string _llmEndpoint = string.Empty;

    // ── v2：自訂對標清單（最多 4 個）+ 資產類焦點卡顯示偏好 ────────────────
    /// <summary>使用者編輯的自訂對標 symbol；XAML 用 ListBox 綁，配 +/− 按鈕。</summary>
    public ObservableCollection<string> CustomBenchmarkSymbols { get; } = [];

    /// <summary>新增自訂對標時的輸入框文字。</summary>
    [ObservableProperty] private string _customBenchmarkInput = string.Empty;

    public bool CanAddCustomBenchmark =>
        !string.IsNullOrWhiteSpace(CustomBenchmarkInput)
        && CustomBenchmarkSymbols.Count < 4
        && !CustomBenchmarkSymbols.Any(s => string.Equals(s, CustomBenchmarkInput.Trim(), StringComparison.OrdinalIgnoreCase));

    partial void OnCustomBenchmarkInputChanged(string _) =>
        OnPropertyChanged(nameof(CanAddCustomBenchmark));

    [ObservableProperty] private bool _isCashFocusEnabled = true;
    [ObservableProperty] private bool _isLiabilityFocusEnabled = true;
    [ObservableProperty] private bool _isRealEstateFocusEnabled = true;
    [ObservableProperty] private bool _isInsuranceFocusEnabled = true;
    [ObservableProperty] private bool _isRetirementFocusEnabled = true;
    [ObservableProperty] private bool _isPhysicalFocusEnabled = true;

    // 注意：partial-changed handler 內部用 `_ = SaveAsync()` 做 discard，所以
    // 參數不能命名 `_`（會跟內部 discard 名稱衝突）。用 `value` 即可。
    partial void OnIsCashFocusEnabledChanged(bool value)       { if (!_isLoading) _ = SaveAsync(); }
    partial void OnIsLiabilityFocusEnabledChanged(bool value)  { if (!_isLoading) _ = SaveAsync(); }
    partial void OnIsRealEstateFocusEnabledChanged(bool value) { if (!_isLoading) _ = SaveAsync(); }
    partial void OnIsInsuranceFocusEnabledChanged(bool value)  { if (!_isLoading) _ = SaveAsync(); }
    partial void OnIsRetirementFocusEnabledChanged(bool value) { if (!_isLoading) _ = SaveAsync(); }
    partial void OnIsPhysicalFocusEnabledChanged(bool value)   { if (!_isLoading) _ = SaveAsync(); }

    // ── 多幣別匯率即時換算 ─────────────────────────────────────────
    // 來源：Frankfurter（CurrencyService.RefreshRatesAsync）。
    // 顯示「上次更新時間 + 各幣別兌台幣匯率」並提供手動刷新按鈕。

    [ObservableProperty] private string _lastFxRefreshDisplay = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefreshFxRates))]
    private bool _isRefreshingFxRates;

    public bool CanRefreshFxRates => !IsRefreshingFxRates;
    public bool CanRefreshUsSymbolDirectory => !IsRefreshingUsSymbolDirectory && _usSymbolDirectory is not null;

    private readonly ObservableCollection<FxRateRow> _fxRateRows = [];
    public ReadOnlyObservableCollection<FxRateRow> FxRateRows { get; }

    private readonly ObservableCollection<string> _supportedCurrencies = [];
    public ReadOnlyObservableCollection<string> SupportedCurrencies { get; }

    public string AppVersion { get; } = ResolveAppVersion();
    public bool IsFugleConfigured => !string.IsNullOrWhiteSpace(FugleApiKey);
    public bool IsTwelveDataConfigured => !string.IsNullOrWhiteSpace(TwelveDataApiKey);
    public bool CanTestTwelveDataConnection =>
        !IsTestingTwelveDataConnection &&
        _twelveDataTester is not null &&
        !string.IsNullOrWhiteSpace(TwelveDataApiKey);
    public bool CanSaveDataSourceSettings =>
        string.IsNullOrWhiteSpace(TwelveDataApiKey) ||
        string.Equals(TwelveDataApiKey.Trim(), _lastTestedTwelveDataApiKey, StringComparison.Ordinal);
    public string TwelveDataQuotaDisplay =>
        $"{Math.Max(0, _settings.Current.TwelveDataQuotaUsed):N0} / {Math.Max(1, _settings.Current.TwelveDataDailyQuota):N0}";

    private double _uiScalePreview = 1.0;
    private string _lastTestedTwelveDataApiKey = string.Empty;

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

    private readonly Assetra.Application.Fx.FxRateHistoryRefresher? _fxRefresher;

    public SettingsViewModel(
        IAppSettingsService settings,
        IThemeService theme,
        ILocalizationService localization,
        ICurrencyService currencyService,
        SyncSettingsViewModel sync,
        ConflictResolutionViewModel conflicts,
        ISnackbarService? snackbar = null,
        IRefreshableSymbolDirectory? usSymbolDirectory = null,
        ITwelveDataConnectionTester? twelveDataTester = null,
        Assetra.Application.Fx.FxRateHistoryRefresher? fxRefresher = null)
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
        _usSymbolDirectory = usSymbolDirectory;
        _twelveDataTester = twelveDataTester;
        _snackbar = snackbar;
        _fxRefresher = fxRefresher;
        Sync = sync;
        Conflicts = conflicts;

        FxRateRows = new ReadOnlyObservableCollection<FxRateRow>(_fxRateRows);
        SupportedCurrencies = new ReadOnlyObservableCollection<string>(_supportedCurrencies);
        foreach (var code in _currencyService.SupportedCurrencies)
            _supportedCurrencies.Add(code);

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
            QuoteProvider = NormalizeTaiwanQuoteProvider(s.QuoteProvider);
            HistoryProvider = string.IsNullOrWhiteSpace(s.HistoryProvider) ? "twse" : s.HistoryProvider;
            FugleApiKey = s.FugleApiKey ?? string.Empty;
            TwelveDataApiKey = s.TwelveDataApiKey ?? string.Empty;
            _lastTestedTwelveDataApiKey = TwelveDataApiKey.Trim();
            OcrTessdataPath = s.OcrTessdataPath ?? string.Empty;
            OcrLanguage = string.IsNullOrWhiteSpace(s.OcrLanguage) ? "eng" : s.OcrLanguage;
            DefaultHomeSection = string.IsNullOrWhiteSpace(s.DefaultHomeSection)
                ? "FinancialOverview"
                : s.DefaultHomeSection;
            LastFxHistoryRefreshAt = s.LastFxHistoryRefreshAt;
            AmtRegularTaxableIncome = s.AmtRegularTaxableIncome;
            AmtRegularIncomeTax = s.AmtRegularIncomeTax;
            AmtInsuranceDeathProceeds = s.AmtInsuranceDeathProceeds;
            AmtInsuranceNonDeathProceeds = s.AmtInsuranceNonDeathProceeds;
            AmtUnlistedSecurityGains = s.AmtUnlistedSecurityGains;
            AmtNonCashDonation = s.AmtNonCashDonation;
            AmtPrivateFundGains = s.AmtPrivateFundGains;
            AmtOverseasTaxCredit = s.AmtOverseasTaxCredit;
            TaxIsMarried = s.TaxIsMarried;
            TaxDependentCount = s.TaxDependentCount;
            TaxPreschoolCount = s.TaxPreschoolCount;
            TaxCollegeStudentCount = s.TaxCollegeStudentCount;
            TaxLongCareCount = s.TaxLongCareCount;
            TaxDisabilityCount = s.TaxDisabilityCount;
            TaxSalaryIncome = s.TaxSalaryIncome;
            TaxInterestIncome = s.TaxInterestIncome;
            TaxRentalExpense = s.TaxRentalExpense;
            TaxUseItemizedDeduction = s.TaxUseItemizedDeduction;
            TaxItemizedDeductionAmount = s.TaxItemizedDeductionAmount;
            TaxDividendSeparate = s.TaxDividendSeparate;
            LlmProvider = s.LlmProvider ?? string.Empty;
            LlmApiKey = s.LlmApiKey ?? string.Empty;
            LlmModel = s.LlmModel ?? string.Empty;
            LlmEndpoint = s.LlmEndpoint ?? string.Empty;

            // v2 — 自訂對標 + 焦點卡顯示偏好
            CustomBenchmarkSymbols.Clear();
            if (s.CustomBenchmarkSymbols is not null)
                foreach (var sym in s.CustomBenchmarkSymbols) CustomBenchmarkSymbols.Add(sym);
            var vis = s.AssetClassFocusVisibility;
            IsCashFocusEnabled       = vis?.GetValueOrDefault("Cash",       true) ?? true;
            IsLiabilityFocusEnabled  = vis?.GetValueOrDefault("Liability",  true) ?? true;
            IsRealEstateFocusEnabled = vis?.GetValueOrDefault("RealEstate", true) ?? true;
            IsInsuranceFocusEnabled  = vis?.GetValueOrDefault("Insurance",  true) ?? true;
            IsRetirementFocusEnabled = vis?.GetValueOrDefault("Retirement", true) ?? true;
            IsPhysicalFocusEnabled   = vis?.GetValueOrDefault("Physical",   true) ?? true;

            DataSourceSaveStatus = string.Empty;
            TwelveDataTestStatus = string.Empty;
            OnPropertyChanged(nameof(IsTwelveDataConfigured));
            OnPropertyChanged(nameof(CanSaveDataSourceSettings));
            OnPropertyChanged(nameof(TwelveDataQuotaDisplay));
            RefreshUsSymbolDirectoryStatus();
            RefreshFxDisplay();
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
        OnPropertyChanged(nameof(CanSaveDataSourceSettings));
        SaveDataSourceSettingsCommand.NotifyCanExecuteChanged();
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

    partial void OnTwelveDataApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsTwelveDataConfigured));
        OnPropertyChanged(nameof(CanTestTwelveDataConnection));
        OnPropertyChanged(nameof(CanSaveDataSourceSettings));
        TestTwelveDataConnectionCommand.NotifyCanExecuteChanged();
        SaveDataSourceSettingsCommand.NotifyCanExecuteChanged();
        if (_isLoading)
            return;

        DataSourceSaveStatus = string.Empty;
        if (!string.Equals(value?.Trim(), _lastTestedTwelveDataApiKey, StringComparison.Ordinal))
        {
            TwelveDataTestStatus = _localization.Get(
                "Settings.TwelveData.NotTested",
                "Twelve Data API key has not been tested yet.");
        }
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

    /// <summary>
    /// Opens the Tesseract tessdata GitHub releases page in the default browser
    /// so the user can download .traineddata files (chi_tra.traineddata for
    /// 繁體中文, eng.traineddata for English etc.) and drop them into the
    /// folder selected via <see cref="BrowseOcrTessdata"/>.
    /// </summary>
    [RelayCommand]
    private void OpenTessdataDownload()
    {
        Process.Start(new ProcessStartInfo("https://github.com/tesseract-ocr/tessdata/tree/main")
        { UseShellExecute = true });
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
            QuoteProvider = NormalizeTaiwanQuoteProvider(QuoteProvider),
            HistoryProvider = HistoryProvider,
            FugleApiKey = FugleApiKey.Trim(),
            TwelveDataApiKey = TwelveDataApiKey.Trim(),
            OcrTessdataPath = OcrTessdataPath?.Trim() ?? string.Empty,
            OcrLanguage = string.IsNullOrWhiteSpace(OcrLanguage) ? "eng" : OcrLanguage.Trim(),
            DefaultHomeSection = string.IsNullOrWhiteSpace(DefaultHomeSection)
                ? "FinancialOverview"
                : DefaultHomeSection,
            AmtRegularTaxableIncome = AmtRegularTaxableIncome,
            AmtRegularIncomeTax = AmtRegularIncomeTax,
            AmtInsuranceDeathProceeds = AmtInsuranceDeathProceeds,
            AmtInsuranceNonDeathProceeds = AmtInsuranceNonDeathProceeds,
            AmtUnlistedSecurityGains = AmtUnlistedSecurityGains,
            AmtNonCashDonation = AmtNonCashDonation,
            AmtPrivateFundGains = AmtPrivateFundGains,
            AmtOverseasTaxCredit = AmtOverseasTaxCredit,
            TaxIsMarried = TaxIsMarried,
            TaxDependentCount = TaxDependentCount,
            TaxPreschoolCount = TaxPreschoolCount,
            TaxCollegeStudentCount = TaxCollegeStudentCount,
            TaxLongCareCount = TaxLongCareCount,
            TaxDisabilityCount = TaxDisabilityCount,
            TaxSalaryIncome = TaxSalaryIncome,
            TaxInterestIncome = TaxInterestIncome,
            TaxRentalExpense = TaxRentalExpense,
            TaxUseItemizedDeduction = TaxUseItemizedDeduction,
            TaxItemizedDeductionAmount = TaxItemizedDeductionAmount,
            TaxDividendSeparate = TaxDividendSeparate,
            LlmProvider = LlmProvider?.Trim() ?? string.Empty,
            LlmApiKey = LlmApiKey?.Trim() ?? string.Empty,
            LlmModel = LlmModel?.Trim() ?? string.Empty,
            LlmEndpoint = LlmEndpoint?.Trim() ?? string.Empty,
            // v2：自訂對標清單 + 資產類焦點卡顯示偏好
            CustomBenchmarkSymbols = CustomBenchmarkSymbols.Count == 0
                ? null
                : CustomBenchmarkSymbols.Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
            AssetClassFocusVisibility = BuildFocusVisibilityMap(),
        };
        await _settings.SaveAsync(updated).ConfigureAwait(true);
    }

    /// <summary>建出 6 個 cell 的 visibility map；全 true 時回 null（節省 JSON 體積）。</summary>
    private Dictionary<string, bool>? BuildFocusVisibilityMap()
    {
        var map = new Dictionary<string, bool>
        {
            ["Cash"] = IsCashFocusEnabled,
            ["Liability"] = IsLiabilityFocusEnabled,
            ["RealEstate"] = IsRealEstateFocusEnabled,
            ["Insurance"] = IsInsuranceFocusEnabled,
            ["Retirement"] = IsRetirementFocusEnabled,
            ["Physical"] = IsPhysicalFocusEnabled,
        };
        // 都是 true 就不存（settings.json 留乾淨）
        return map.Values.All(v => v) ? null : map;
    }

    [RelayCommand]
    private void AddCustomBenchmark()
    {
        if (!CanAddCustomBenchmark) return;
        CustomBenchmarkSymbols.Add(CustomBenchmarkInput.Trim());
        CustomBenchmarkInput = string.Empty;
        OnPropertyChanged(nameof(CanAddCustomBenchmark));
        if (!_isLoading) _ = SaveAsync();
    }

    [RelayCommand]
    private void RemoveCustomBenchmark(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return;
        if (CustomBenchmarkSymbols.Remove(symbol))
        {
            OnPropertyChanged(nameof(CanAddCustomBenchmark));
            if (!_isLoading) _ = SaveAsync();
        }
    }

    partial void OnLlmProviderChanged(string value)  { if (!_isLoading) _ = SaveAsync(); }
    partial void OnLlmApiKeyChanged(string value)    { if (!_isLoading) _ = SaveAsync(); }
    partial void OnLlmModelChanged(string value)     { if (!_isLoading) _ = SaveAsync(); }
    partial void OnLlmEndpointChanged(string value)  { if (!_isLoading) _ = SaveAsync(); }

    public static IReadOnlyList<ProviderOption> SupportedLlmProviders { get; } =
    [
        new("", "停用 (Rule-based only)"),
        new("openai", "OpenAI"),
        new("ollama", "Ollama (local)"),
    ];

    // ── AMT 彙整 / 個人稅務 — 全部走 SaveOnChange 模式 ───────────────────
    // Negative-clamp + autosave；簡單一致。

    partial void OnAmtRegularTaxableIncomeChanged(decimal v)  { if (v < 0m) { AmtRegularTaxableIncome = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnAmtRegularIncomeTaxChanged(decimal v)      { if (v < 0m) { AmtRegularIncomeTax = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnAmtInsuranceDeathProceedsChanged(decimal v)    { if (v < 0m) { AmtInsuranceDeathProceeds = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnAmtInsuranceNonDeathProceedsChanged(decimal v) { if (v < 0m) { AmtInsuranceNonDeathProceeds = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnAmtUnlistedSecurityGainsChanged(decimal v) { if (v < 0m) { AmtUnlistedSecurityGains = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnAmtNonCashDonationChanged(decimal v)       { if (v < 0m) { AmtNonCashDonation = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnAmtPrivateFundGainsChanged(decimal v)      { if (v < 0m) { AmtPrivateFundGains = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnAmtOverseasTaxCreditChanged(decimal v)     { if (v < 0m) { AmtOverseasTaxCredit = 0m; return; } if (!_isLoading) _ = SaveAsync(); }

    partial void OnTaxIsMarriedChanged(bool v)                { if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxDependentCountChanged(int v)            { if (v < 0) { TaxDependentCount = 0; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxPreschoolCountChanged(int v)            { if (v < 0) { TaxPreschoolCount = 0; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxCollegeStudentCountChanged(int v)       { if (v < 0) { TaxCollegeStudentCount = 0; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxLongCareCountChanged(int v)             { if (v < 0) { TaxLongCareCount = 0; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxDisabilityCountChanged(int v)           { if (v < 0) { TaxDisabilityCount = 0; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxSalaryIncomeChanged(decimal v)          { if (v < 0m) { TaxSalaryIncome = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxInterestIncomeChanged(decimal v)        { if (v < 0m) { TaxInterestIncome = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxRentalExpenseChanged(decimal v)         { if (v < 0m) { TaxRentalExpense = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxUseItemizedDeductionChanged(bool v)     { if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxItemizedDeductionAmountChanged(decimal v) { if (v < 0m) { TaxItemizedDeductionAmount = 0m; return; } if (!_isLoading) _ = SaveAsync(); }
    partial void OnTaxDividendSeparateChanged(bool v)         { if (!_isLoading) _ = SaveAsync(); }

    partial void OnDefaultHomeSectionChanged(string value)
    {
        if (_isLoading) return;
        _ = SaveAsync();
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

    [RelayCommand(CanExecute = nameof(CanSaveDataSourceSettings))]
    private async Task SaveDataSourceSettingsAsync()
    {
        if (!CanSaveDataSourceSettings)
        {
            DataSourceSaveStatus = _localization.Get(
                "Settings.TwelveData.TestBeforeSave",
                "Test the Twelve Data API key before saving this provider.");
            return;
        }

        await SaveAsync(showDataSourceSuccess: true);
    }

    [RelayCommand(CanExecute = nameof(CanTestTwelveDataConnection))]
    private async Task TestTwelveDataConnectionAsync()
    {
        if (_twelveDataTester is null || IsTestingTwelveDataConnection)
            return;

        IsTestingTwelveDataConnection = true;
        try
        {
            var key = TwelveDataApiKey.Trim();
            var result = await _twelveDataTester.TestAsync(key).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                _lastTestedTwelveDataApiKey = key;
                TwelveDataTestStatus = _localization.Get(
                    "Settings.TwelveData.TestSucceeded",
                    "Twelve Data connection test succeeded.");
            }
            else
            {
                _lastTestedTwelveDataApiKey = string.Empty;
                TwelveDataTestStatus = result.Error?.Message
                    ?? _localization.Get("Settings.TwelveData.TestFailed", "Twelve Data connection test failed.");
            }
        }
        catch (Exception ex)
        {
            _lastTestedTwelveDataApiKey = string.Empty;
            TwelveDataTestStatus = $"{_localization.Get("Settings.TwelveData.TestFailed", "Twelve Data connection test failed.")} {ex.Message}";
        }
        finally
        {
            IsTestingTwelveDataConnection = false;
            OnPropertyChanged(nameof(CanSaveDataSourceSettings));
            OnPropertyChanged(nameof(TwelveDataQuotaDisplay));
            SaveDataSourceSettingsCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsTestingTwelveDataConnectionChanged(bool value)
    {
        TestTwelveDataConnectionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task RefreshFxRatesAsync()
    {
        if (IsRefreshingFxRates) return;
        IsRefreshingFxRates = true;
        try
        {
            await _currencyService.RefreshRatesAsync().ConfigureAwait(true);
            RefreshFxDisplay();
        }
        catch (Exception ex)
        {
            ShowSaveFailure(ex);
        }
        finally
        {
            IsRefreshingFxRates = false;
        }
    }

    [RelayCommand]
    private async Task RefreshUsSymbolDirectoryAsync()
    {
        if (_usSymbolDirectory is null || IsRefreshingUsSymbolDirectory)
            return;

        IsRefreshingUsSymbolDirectory = true;
        try
        {
            var updated = await _usSymbolDirectory.RefreshAsync(force: true).ConfigureAwait(true);
            RefreshUsSymbolDirectoryStatus(updated
                ? _localization.Get("Settings.UsSymbolDirectory.Updated", "US symbol directory updated.")
                : _localization.Get("Settings.UsSymbolDirectory.NoChange", "US symbol directory was already current."));
        }
        catch (Exception ex)
        {
            var message = $"{_localization.Get("Settings.UsSymbolDirectory.Failed", "US symbol directory refresh failed.")} {ex.Message}";
            UsSymbolDirectoryStatus = message;
            _snackbar?.Error(message);
        }
        finally
        {
            IsRefreshingUsSymbolDirectory = false;
        }
    }

    /// <summary>
    /// P4.1d — manual trigger for the FX history refresher. Same pipeline as
    /// the startup auto-refresh; the user-facing button just makes it on-demand.
    /// On success <see cref="LastFxRefreshAt"/> updates to current time and
    /// persists to AppSettings via the refresher's own settings hook.
    /// </summary>
    [RelayCommand]
    private Task RefreshFxHistoryAsync() => RunFxRefreshAsync(daysBack: 7);

    /// <summary>
    /// P4.1f — deep backfill (365 days). Useful for users who want long
    /// historical reports (year-end snapshots, multi-year trend charts) and
    /// don't want to wait for the daily 7-day pull to gradually fill in.
    /// </summary>
    [RelayCommand]
    private Task RefreshFxHistoryDeepAsync() => RunFxRefreshAsync(daysBack: 365);

    private async Task RunFxRefreshAsync(int daysBack)
    {
        if (_fxRefresher is null || IsRefreshingFxHistory) return;

        IsRefreshingFxHistory = true;
        try
        {
            var baseCcy = string.IsNullOrWhiteSpace(BaseCurrency) ? "TWD" : BaseCurrency;
            await _fxRefresher.RefreshAsync(
                baseCcy,
                Assetra.Application.Fx.FxRateHistoryRefresher.DefaultForeignCurrencies,
                daysBack: daysBack).ConfigureAwait(true);
            LastFxHistoryRefreshAt = _settings.Current?.LastFxHistoryRefreshAt;
            _snackbar?.Success(_localization.Get(
                "Settings.FxHistory.RefreshedSuccess", "匯率歷史已更新"));
        }
        catch (Exception ex)
        {
            _snackbar?.Error(_localization.Get(
                "Settings.FxHistory.RefreshedFailed", "匯率歷史更新失敗") + " " + ex.Message);
        }
        finally
        {
            IsRefreshingFxHistory = false;
        }
    }

    partial void OnIsRefreshingUsSymbolDirectoryChanged(bool value)
    {
        RefreshUsSymbolDirectoryCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFxDisplay()
    {
        _fxRateRows.Clear();
        var rates = _currencyService.ExchangeRates;
        if (rates is null) return;
        foreach (var kv in rates.OrderBy(k => k.Key))
        {
            if (string.Equals(kv.Key, "TWD", StringComparison.OrdinalIgnoreCase)) continue;
            _fxRateRows.Add(new FxRateRow(kv.Key, kv.Value));
        }

        var iso = _settings.Current.LastFxRefreshUtc;
        if (string.IsNullOrWhiteSpace(iso) ||
            !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var utc))
        {
            LastFxRefreshDisplay = _localization.Get("Settings.Fx.NeverRefreshed", "Never refreshed");
            return;
        }
        LastFxRefreshDisplay = utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private void RefreshUsSymbolDirectoryStatus(string? prefix = null)
    {
        if (_usSymbolDirectory is null)
        {
            UsSymbolDirectoryStatus = _localization.Get(
                "Settings.UsSymbolDirectory.Disabled",
                "US symbol directory is not enabled.");
            return;
        }

        var updatedAt = _usSymbolDirectory.LastUpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            ?? _localization.Get("Settings.UsSymbolDirectory.NeverUpdated", "Never updated");
        var status = string.Format(
            _localization.Get("Settings.UsSymbolDirectory.Status", "{0:N0} symbols, last updated {1}."),
            _usSymbolDirectory.Count,
            updatedAt);
        UsSymbolDirectoryStatus = string.IsNullOrWhiteSpace(prefix) ? status : $"{prefix} {status}";
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

    // ── Twelve Data help dialog (mirrors Fugle help triad) ─────────────────
    [RelayCommand]
    private void OpenTwelveDataHelp() => IsTwelveDataHelpOpen = true;

    [RelayCommand]
    private void CloseTwelveDataHelp() => IsTwelveDataHelpOpen = false;

    [RelayCommand]
    private void OpenTwelveDataSite()
    {
        // Lands on the homepage; signed-in users get redirected to the dashboard
        // (where the API key lives). Anonymous users see Register / Sign-in CTA.
        Process.Start(new ProcessStartInfo("https://twelvedata.com/") { UseShellExecute = true });
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

    private static string NormalizeTaiwanQuoteProvider(string? provider) =>
        string.Equals(provider, "fugle", StringComparison.OrdinalIgnoreCase)
            ? "fugle"
            : "official";

    public void Dispose()
    {
        _theme.ThemeChanged -= OnThemeChanged;
    }
}
