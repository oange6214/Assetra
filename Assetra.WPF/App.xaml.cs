using System.IO;
using System.Windows;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using Assetra.Core.Interfaces;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Alerts;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Infrastructure;
using Assetra.WPF.Infrastructure.Converters;
using Assetra.WPF.Shell;
using Wpf.Ui.Appearance;

namespace Assetra.WPF;

public partial class App : Application
{
    private IHost _host = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await StartupAsync();
        }
        catch (Exception ex)
        {
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

        // Show main window, close splash
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        splash.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }
        base.OnExit(e);
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
