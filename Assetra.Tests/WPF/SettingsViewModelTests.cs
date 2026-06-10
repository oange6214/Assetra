using System.IO;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Settings;
using Assetra.WPF.Infrastructure;
using Moq;
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

    private SyncSettingsViewModel CreateSyncVm()
    {
        var queue = new Assetra.Application.Sync.CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object);
        var coordinator = new SyncCoordinator(
            _mockSettings.Object,
            queue,
            new Mock<IConflictResolver>().Object,
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sync-meta-{Guid.NewGuid():N}.json"));
        return new SyncSettingsViewModel(_mockSettings.Object, coordinator, new SyncPassphraseCache());
    }

    private ConflictResolutionViewModel CreateConflictsVm()
    {
        var queue = new Assetra.Application.Sync.CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object);
        return new ConflictResolutionViewModel(queue, queue);
    }

    private SettingsViewModel CreateVm(
        ISnackbarService? snackbar = null,
        ITwelveDataConnectionTester? twelveDataTester = null,
        IPortfolioSnapshotRebuildService? rebuild = null,
        IPortfolioPositionLogRepository? positionLogs = null,
        IDatabaseBackupService? backup = null) =>
        new(_mockSettings.Object, _mockTheme.Object,
            _mockLocalization.Object, _mockCurrency.Object,
            CreateSyncVm(), CreateConflictsVm(), snackbar, twelveDataTester: twelveDataTester,
            rebuild: rebuild, positionLogs: positionLogs, backup: backup);

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

    [Fact]
    public void ShowFugleKeyMissingWarning_TrueOnlyWhenFugleSelectedAndKeyBlank()
    {
        // WHY: 選了 Fugle 卻沒填金鑰時會悄悄回退到 TWSE/TPEX 官方資料；warning 行讓使用者
        // 知道為何沒套用 Fugle。其他組合（官方 provider，或 Fugle 已填金鑰）都不該顯示。
        var vm = CreateVm();

        // 預設 provider = official → 不顯示。
        Assert.False(vm.ShowFugleKeyMissingWarning);

        // 選 Fugle 但金鑰空白 → 顯示。
        vm.QuoteProvider = "fugle";
        Assert.True(vm.ShowFugleKeyMissingWarning);

        // 補上金鑰 → 不再顯示。
        vm.FugleApiKey = "demo-key";
        Assert.False(vm.ShowFugleKeyMissingWarning);

        // 改回官方來源（即使金鑰被清空）→ 不顯示。
        vm.FugleApiKey = string.Empty;
        vm.QuoteProvider = "official";
        Assert.False(vm.ShowFugleKeyMissingWarning);
    }

    [Fact]
    public async Task SaveDataSourceSettingsCommand_TwelveDataRequiresSuccessfulTestBeforeSave()
    {
        var saved = new List<AppSettings>();
        _mockSettings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(saved.Add)
            .Returns(Task.CompletedTask);
        var tester = new FakeTwelveDataTester(success: true);
        var vm = CreateVm(twelveDataTester: tester);

        vm.TwelveDataApiKey = "demo-key";

        Assert.False(vm.CanSaveDataSourceSettings);
        await vm.SaveDataSourceSettingsCommand.ExecuteAsync(null);
        Assert.Empty(saved);

        await vm.TestTwelveDataConnectionCommand.ExecuteAsync(null);
        Assert.True(vm.CanSaveDataSourceSettings);

        await vm.SaveDataSourceSettingsCommand.ExecuteAsync(null);

        var settings = Assert.Single(saved);
        Assert.Equal("official", settings.QuoteProvider);
        Assert.Equal("demo-key", settings.TwelveDataApiKey);
        Assert.Equal(1, tester.CallCount);
    }

    [Fact]
    public async Task BaseCurrencyChanged_PersistsSetting()
    {
        var saved = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockSettings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .Callback<AppSettings>(s => saved.TrySetResult(s))
            .Returns(Task.CompletedTask);
        var vm = CreateVm();

        vm.BaseCurrency = "USD";

        var settings = await saved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("USD", settings.BaseCurrency);
    }

    [Fact]
    public async Task BaseCurrencyChanged_SaveFailureSetsStatusAndSnackbar()
    {
        _mockSettings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>()))
            .ThrowsAsync(new IOException("disk full"));
        var snackbar = new Mock<ISnackbarService>();
        var vm = CreateVm(snackbar.Object);

        vm.BaseCurrency = "USD";

        await WaitForAsync(() => vm.DataSourceSaveStatus.Contains("disk full", StringComparison.Ordinal));
        snackbar.Verify(s => s.Error(It.Is<string>(message => message.Contains("disk full"))), Times.Once);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    // ── B Stage 2 — Rebuild snapshot history (preview / confirm / cancel) ─────────

    private static readonly DateOnly RebuildFrom = new(2024, 1, 1);
    private static readonly DateOnly RebuildTo = new(2024, 1, 5);

    private static SnapshotRebuildDayResult RebuiltDay(DateOnly date) =>
        new(date, OldMarketValue: null, NewMarketValue: 100m, NewCash: 10m, NewEquity: 100m,
            NewLiability: 0m, RebuildDayStatus.Rebuilt);

    private static SnapshotRebuildDayResult NoFxDay(DateOnly date) =>
        new(date, OldMarketValue: null, NewMarketValue: null, NewCash: null, NewEquity: null,
            NewLiability: null, RebuildDayStatus.SkippedNoFx);

    [Fact]
    public async Task PreviewRebuild_OpensDialogWithCounts_AndWritesNothing()
    {
        // WHY: the preview must run the engine in DRY-RUN only — surfacing the rebuilt / skipped
        // counts (incl. NoFxCount, which drives the "refresh FX first" warning) so the user can
        // make an informed decision — without persisting anything. We feed a report with 2 Rebuilt
        // and 1 SkippedNoFx day and assert the VM mirrors those counts, opens the dialog, and that
        // the engine was invoked exactly once with dryRun:true (no real write).
        var report = new SnapshotRebuildReport(
            RebuildFrom, RebuildTo, DryRun: true,
            [
                RebuiltDay(new DateOnly(2024, 1, 1)),
                RebuiltDay(new DateOnly(2024, 1, 2)),
                NoFxDay(new DateOnly(2024, 1, 3)),
            ]);
        var rebuild = new FakeRebuildService(report);
        var logs = new FakeLogRepository(new DateOnly(2024, 1, 1));

        var vm = CreateVm(rebuild: rebuild, positionLogs: logs, backup: new RecordingBackupService(new List<string>()));

        await vm.PreviewRebuildSnapshotHistoryCommand.ExecuteAsync(null);

        Assert.True(vm.IsRebuildPreviewOpen);
        Assert.Equal(3, vm.RebuildTotalDays);
        Assert.Equal(2, vm.RebuildRebuiltCount);
        Assert.Equal(1, vm.RebuildNoFxCount);
        Assert.True(vm.ShowRebuildNoFxWarning);
        // dry-run only: engine called once, and never with dryRun:false (no write).
        Assert.Equal(1, rebuild.DryRunCalls);
        Assert.Equal(0, rebuild.WriteCalls);
    }

    [Fact]
    public async Task PreviewRebuild_NoPositionLogs_ShowsSnackbarAndDoesNotOpenDialog()
    {
        // WHY: with no position history there is nothing to rebuild; the VM must short-circuit with
        // a "no data" snackbar and NOT invoke the engine or open the dialog.
        var rebuild = new FakeRebuildService(EmptyReport());
        var logs = new FakeLogRepository(); // zero entries
        var snackbar = new Mock<ISnackbarService>();

        var vm = CreateVm(snackbar.Object, rebuild: rebuild, positionLogs: logs,
            backup: new RecordingBackupService(new List<string>()));

        await vm.PreviewRebuildSnapshotHistoryCommand.ExecuteAsync(null);

        Assert.False(vm.IsRebuildPreviewOpen);
        Assert.Equal(0, rebuild.DryRunCalls);
        snackbar.Verify(s => s.Warning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmRebuild_BacksUpBeforeWriting_ThenClosesDialog()
    {
        // WHY: the whole safety contract is "back up the DB, THEN write". If a real write ever
        // preceded the backup, a failed rebuild could corrupt history with no recovery point. We
        // record backup + write into ONE ordered timeline and assert backup is strictly first.
        // The dialog must also close and a success snackbar fire.
        var events = new List<string>();
        var backup = new RecordingBackupService(events);
        var rebuild = new OrderRecordingRebuildService(events, RebuildFrom, RebuildTo);
        var logs = new FakeLogRepository(new DateOnly(2024, 1, 1));
        var snackbar = new Mock<ISnackbarService>();

        var vm = CreateVm(snackbar.Object, rebuild: rebuild, positionLogs: logs, backup: backup);

        // Preview first to populate the pending report, then confirm.
        await vm.PreviewRebuildSnapshotHistoryCommand.ExecuteAsync(null);
        await vm.ConfirmRebuildSnapshotHistoryCommand.ExecuteAsync(null);

        Assert.Equal(["backup", "write"], events);          // backup strictly before write
        Assert.False(vm.IsRebuildPreviewOpen);              // dialog closed on success
        Assert.False(vm.IsRebuildBusy);
        snackbar.Verify(s => s.Success(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmRebuild_BackupFailure_KeepsDialogOpen_AndDoesNotWrite()
    {
        // WHY: if the backup throws, we must NOT proceed to write and must keep the dialog open so
        // the user sees the failure (error snackbar) rather than silently losing their confirmation.
        var events = new List<string>();
        var backup = new ThrowingBackupService();
        var rebuild = new OrderRecordingRebuildService(events, RebuildFrom, RebuildTo);
        var logs = new FakeLogRepository(new DateOnly(2024, 1, 1));
        var snackbar = new Mock<ISnackbarService>();

        var vm = CreateVm(snackbar.Object, rebuild: rebuild, positionLogs: logs, backup: backup);

        await vm.PreviewRebuildSnapshotHistoryCommand.ExecuteAsync(null);
        await vm.ConfirmRebuildSnapshotHistoryCommand.ExecuteAsync(null);

        Assert.Equal(0, rebuild.WriteCalls);     // never wrote
        Assert.True(vm.IsRebuildPreviewOpen);     // dialog stays open so the error is visible
        Assert.False(vm.IsRebuildBusy);
        snackbar.Verify(s => s.Error(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CancelRebuild_ClosesDialog_AndWritesNothing()
    {
        // WHY: cancelling must close the dialog and discard the pending report — a subsequent
        // confirm (without re-previewing) must be a no-op, never writing.
        var events = new List<string>();
        var backup = new RecordingBackupService(events);
        var rebuild = new OrderRecordingRebuildService(events, RebuildFrom, RebuildTo);
        var logs = new FakeLogRepository(new DateOnly(2024, 1, 1));

        var vm = CreateVm(rebuild: rebuild, positionLogs: logs, backup: backup);

        await vm.PreviewRebuildSnapshotHistoryCommand.ExecuteAsync(null);
        vm.CancelRebuildSnapshotHistoryCommand.Execute(null);

        Assert.False(vm.IsRebuildPreviewOpen);

        // Pending report cleared → confirm now does nothing (no backup, no write).
        await vm.ConfirmRebuildSnapshotHistoryCommand.ExecuteAsync(null);
        Assert.Empty(events);
        Assert.Equal(0, rebuild.WriteCalls);
    }

    private static SnapshotRebuildReport EmptyReport() =>
        new(RebuildFrom, RebuildTo, DryRun: true, []);

    /// <summary>Returns a fixed dry-run report; records how many dry-run vs write calls it saw.</summary>
    private sealed class FakeRebuildService(SnapshotRebuildReport report) : IPortfolioSnapshotRebuildService
    {
        public int DryRunCalls { get; private set; }
        public int WriteCalls { get; private set; }

        public Task<SnapshotRebuildReport> RebuildAsync(
            DateOnly from, DateOnly to, bool dryRun, CancellationToken ct = default)
        {
            if (dryRun)
                DryRunCalls++;
            else
                WriteCalls++;
            return Task.FromResult(report with { From = from, To = to, DryRun = dryRun });
        }
    }

    /// <summary>
    /// Records a "write" event (only for the real, dryRun:false call) into a shared ordered
    /// timeline so a test can assert backup-before-write. Dry-run calls return a report but emit no
    /// event (they don't persist).
    /// </summary>
    private sealed class OrderRecordingRebuildService(
        List<string> events, DateOnly from, DateOnly to) : IPortfolioSnapshotRebuildService
    {
        public int WriteCalls { get; private set; }

        public Task<SnapshotRebuildReport> RebuildAsync(
            DateOnly f, DateOnly t, bool dryRun, CancellationToken ct = default)
        {
            if (!dryRun)
            {
                WriteCalls++;
                events.Add("write");
            }
            var days = new List<SnapshotRebuildDayResult> { RebuiltDay(from) };
            return Task.FromResult(new SnapshotRebuildReport(from, to, dryRun, days));
        }
    }

    /// <summary>Records a "backup" event into the shared timeline; returns a fixed file path.</summary>
    private sealed class RecordingBackupService(List<string> events) : IDatabaseBackupService
    {
        public Task<string> BackupAsync(CancellationToken ct = default)
        {
            events.Add("backup");
            return Task.FromResult(@"C:\data\assetra.db.bak-20240101-000000");
        }
    }

    private sealed class ThrowingBackupService : IDatabaseBackupService
    {
        public Task<string> BackupAsync(CancellationToken ct = default) =>
            throw new IOException("backup disk full");
    }

    /// <summary>Minimal position-log repo: GetAllAsync returns logs dated on the supplied days.</summary>
    private sealed class FakeLogRepository : IPortfolioPositionLogRepository
    {
        private readonly IReadOnlyList<PortfolioPositionLog> _logs;

        public FakeLogRepository(params DateOnly[] dates) =>
            _logs = dates.Select(d =>
                new PortfolioPositionLog(Guid.NewGuid(), d, Guid.NewGuid(), "2330", "TWSE", 1, 100m)).ToList();

        public Task<IReadOnlyList<PortfolioPositionLog>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(_logs);
        public Task<bool> HasAnyAsync(CancellationToken ct = default) => Task.FromResult(_logs.Count > 0);
        public Task LogAsync(PortfolioPositionLog entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogBatchAsync(IEnumerable<PortfolioPositionLog> entries, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeTwelveDataTester(bool success) : ITwelveDataConnectionTester
    {
        public int CallCount { get; private set; }

        public Task<MarketDataResult<EquityQuote>> TestAsync(string apiKey, CancellationToken ct = default)
        {
            CallCount++;
            var key = new EquityInstrumentKey("AAPL", "NASDAQ");
            var result = success
                ? MarketDataResult<EquityQuote>.Success(new EquityQuote(
                    key,
                    100m,
                    previousClose: 99m,
                    change: 1m,
                    changePercent: 1m,
                    currency: "USD",
                    updatedAt: DateTimeOffset.UnixEpoch,
                    sourceProvider: "Twelve Data",
                    isDelayed: true))
                : MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                    MarketDataErrorCode.MissingApiKey,
                    "Missing key",
                    Provider: "Twelve Data",
                    Instrument: key));

            return Task.FromResult(result);
        }
    }
}
