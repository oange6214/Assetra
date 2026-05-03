using System.IO;
using System.Windows;
using Assetra.Core.Interfaces;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Alerts;
using Assetra.WPF.Features.Categories;
using Assetra.WPF.Features.FinancialOverview;
using Assetra.WPF.Features.Insurance;
using Assetra.WPF.Features.PhysicalAsset;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.RealEstate;
using Assetra.WPF.Features.Recurring;
using Assetra.WPF.Features.Retirement;
using Assetra.WPF.Infrastructure;
using Assetra.WPF.Infrastructure.Converters;
using Assetra.WPF.Shell;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using Velopack;
using Wpf.Ui.Appearance;

namespace Assetra.WPF;

public partial class App : System.Windows.Application
{
    private IHost _host = null!;
    private bool _startupCompleted;
    private static string StartupMarkerPath =>
        Path.Combine(AppRuntimePaths.Resolve().DataDir, "startup.pending");

    protected override async void OnStartup(StartupEventArgs e)
    {
        VelopackApp.Build().Run();
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        var shouldTryRecoveryUpdate = File.Exists(StartupMarkerPath);
        WriteStartupMarker();
        try
        {
#if !DEBUG
            if (shouldTryRecoveryUpdate &&
                await TryApplyRecoveryUpdateAsync().ConfigureAwait(true))
            {
                Shutdown(0);
                return;
            }
#endif

            await StartupAsync();
            _startupCompleted = true;
            ClearStartupMarker();
        }
        catch (Exception ex)
        {
#if !DEBUG
            if (await TryApplyRecoveryUpdateAsync().ConfigureAwait(true))
            {
                Shutdown(0);
                return;
            }
#endif

            MessageBox.Show(
                $"應用程式啟動失敗：{ex.Message}",
                "Assetra",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartupAsync()
    {
        // Configure LiveCharts with a CJK-capable typeface
        // Default SkiaSharp font (Arial/Helvetica) lacks many CJK glyphs.
        // "Microsoft YaHei UI" covers all Chinese characters used in tooltips/legends.
        LiveCharts.Configure(config =>
            config.HasGlobalSKTypeface(
                SKTypeface.FromFamilyName("Microsoft YaHei UI")
                ?? SKTypeface.Default));

        // Apply saved theme & language BEFORE splash so it renders correctly
        ApplyEarlySettings();

        // Show splash screen immediately
        var splash = new Shell.SplashScreen();
        splash.Show();

        splash.Advance("Splash.Init");
        _host = AppBootstrapper.Build();

        // StartAsync runs IHostedServices in registration order:
        //   1. DbInitializerService  — SQLite PRAGMAs + first-run Stockra import
        //   2. MarketDataHostedService — IStockService.Start()
        splash.Advance("Splash.Services");
        await _host.StartAsync();

        // Wire CurrencyConverter's static Service so all XAML bindings can format currency
        CurrencyConverter.Service = _host.Services.GetRequiredService<ICurrencyService>();

        splash.Advance("Splash.Portfolio");
        var portfolioVm = _host.Services.GetRequiredService<PortfolioViewModel>();
        await portfolioVm.LoadAsync();

        splash.Advance("Splash.Alerts");
        var alertsVm = _host.Services.GetRequiredService<AlertsViewModel>();
        await alertsVm.LoadAsync();

        splash.Advance("Splash.Categories");
        var categoriesVm = _host.Services.GetRequiredService<CategoriesViewModel>();
        await categoriesVm.LoadAsync();

        splash.Advance("Splash.Recurring");
        var recurringVm = _host.Services.GetRequiredService<RecurringViewModel>();
        await recurringVm.LoadAsync();

        // Pre-load multi-asset hub VMs and FinancialOverview so the dashboards
        // are populated the first time the user navigates into them — before
        // this, the singleton VMs were constructed but no LoadAsync ever fired
        // until a hub view became visible, which left FinancialOverview with an
        // empty snapshot of Portfolio.Positions.
        splash.Advance("Splash.MultiAsset");
        _host.Services.GetRequiredService<RealEstateViewModel>().LoadCommand.Execute(null);
        _host.Services.GetRequiredService<InsurancePolicyViewModel>().LoadCommand.Execute(null);
        _host.Services.GetRequiredService<RetirementViewModel>().LoadCommand.Execute(null);
        _host.Services.GetRequiredService<PhysicalAssetViewModel>().LoadCommand.Execute(null);

        splash.Advance("Splash.FinancialOverview");
        var financialOverviewVm = _host.Services.GetRequiredService<FinancialOverviewViewModel>();
        await financialOverviewVm.LoadAsync();

        // Show main window, close splash
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        splash.Close();

#if !DEBUG
        _ = CheckForUpdatesInBackgroundAsync();
#endif
    }

    /// <summary>
    /// Swallows known-benign third-party NREs that fire on navigation/page swap.
    /// Currently filters: <see cref="LiveChartsCore.SkiaSharpView.WPF"/>'s
    /// <c>CompositionTargetTicker.DisposeTicker</c> double-dispose race
    /// (library bug present at least up to 2.0.2 — `_canvas` is already null
    /// when DisposeTicker runs a second time during rapid Loaded/Unloaded
    /// cycles).  These are non-fatal: the ticker is being torn down anyway.
    /// </summary>
    private static void OnDispatcherUnhandledException(
        object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is NullReferenceException
            && e.Exception.StackTrace is { } stack
            && stack.Contains("CompositionTargetTicker", StringComparison.Ordinal))
        {
            Serilog.Log.Debug(e.Exception,
                "Swallowed LiveCharts CompositionTargetTicker dispose NRE (benign)");
            e.Handled = true;
        }
    }

#if !DEBUG
    private static UpdateManager CreateUpdateManager() =>
        new(new GithubSource("https://github.com/oange6214/Assetra", null, false));

    private static async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var mgr = CreateUpdateManager();
            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion is null) return;
            await mgr.DownloadUpdatesAsync(newVersion);
            var result = MessageBox.Show(
                $"發現新版本 {newVersion.TargetFullRelease.Version}，已下載完成。立即重新啟動以套用更新？",
                "Assetra 更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
                mgr.ApplyUpdatesAndRestart(newVersion);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to check for updates in background");
        }
    }

    private static async Task<bool> TryApplyRecoveryUpdateAsync()
    {
        try
        {
            var mgr = CreateUpdateManager();
            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion is null) return false;

            await mgr.DownloadUpdatesAsync(newVersion);
            var result = MessageBox.Show(
                $"啟動時發生錯誤，但偵測到新版本 {newVersion.TargetFullRelease.Version}。要立即更新並重新啟動嗎？",
                "Assetra 修復更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                mgr.ApplyUpdatesAndRestart(newVersion);
                return true;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to recover from startup failure via update");
        }

        return false;
    }
#endif

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        if (_startupCompleted)
            ClearStartupMarker();

        base.OnExit(e);
    }

    private static void WriteStartupMarker()
    {
        try
        {
            File.WriteAllText(StartupMarkerPath, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to write startup marker");
        }
    }

    private static void ClearStartupMarker()
    {
        try
        {
            if (File.Exists(StartupMarkerPath))
                File.Delete(StartupMarkerPath);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to clear startup marker");
        }
    }

    /// <summary>
    /// Read saved theme and language from disk and apply them to the
    /// Application ResourceDictionaries before any window is shown.
    /// This ensures the splash screen renders with the user's chosen
    /// theme and locale instead of the hardcoded defaults in App.xaml.
    /// </summary>
    private static void ApplyEarlySettings()
    {
        // Theme
        try
        {
            var themeFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Assetra", "theme.txt");

            if (File.Exists(themeFile)
                && Enum.TryParse<ApplicationTheme>(File.ReadAllText(themeFile).Trim(), out var theme))
            {
                // Swap WPF-UI's own ThemesDictionary (resource-only, no DWM ops)
                var wpfUiThemeName = theme == ApplicationTheme.Light ? "Light" : "Dark";
                var wpfUiUri = new Uri(
                    $"pack://application:,,,/Wpf.Ui;component/Resources/Theme/{wpfUiThemeName}.xaml",
                    UriKind.Absolute);
                var dicts = Current.Resources.MergedDictionaries;
                for (var i = 0; i < dicts.Count; i++)
                {
                    var src = dicts[i].Source?.ToString();
                    if (src is not null
                        && src.Contains("wpf.ui;", StringComparison.OrdinalIgnoreCase)
                        && src.Contains("theme", StringComparison.OrdinalIgnoreCase))
                    {
                        dicts[i] = new ResourceDictionary { Source = wpfUiUri };
                        break;
                    }
                }

                // In-place swap of our custom palette (Dark.xaml ↔ Light.xaml)
                var paletteName = theme == ApplicationTheme.Light ? "Light" : "Dark";
                var newDict = new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Assetra.WPF;component/Themes/{paletteName}.xaml")
                };
                for (var i = 0; i < dicts.Count; i++)
                {
                    var src = dicts[i].Source?.ToString();
                    if (src is not null
                        && (src.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase)
                         || src.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)))
                    {
                        dicts[i] = newDict;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to apply saved theme; falling back to App.xaml default");
        }

        // Language
        try
        {
            var saved = AppSettingsService.LoadSettings();
            if (!string.IsNullOrWhiteSpace(saved.Language) && saved.Language != "zh-TW")
            {
                var uri = new Uri($"pack://application:,,,/Assetra.WPF;component/Languages/{saved.Language}.xaml");
                var dict = new ResourceDictionary { Source = uri };

                var merged = Current.Resources.MergedDictionaries;
                var old = merged.FirstOrDefault(d =>
                    d.Source?.OriginalString.Contains("/Languages/") == true);
                if (old is not null)
                    merged.Remove(old);
                merged.Add(dict);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to apply saved language; falling back to App.xaml default (zh-TW)");
        }
    }
}
