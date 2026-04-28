using Assetra.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// 背景定時觸發 <see cref="SyncCoordinator.SyncAsync"/>。
/// <para>
/// 必要條件：<see cref="Core.Models.AppSettings.SyncEnabled"/> = true、間隔 &gt; 0、密語已快取
/// （使用者最近一次手動同步成功後 <see cref="SyncPassphraseCache"/> 會被填）。任一不滿足就跳過該 tick，不報錯。
/// </para>
/// <para>
/// 失敗只記 log，不擲——避免 hosted service crash 拖垮整個 app。
/// </para>
/// </summary>
public sealed class BackgroundSyncService : BackgroundService
{
    private static readonly TimeSpan PollGranularity = TimeSpan.FromSeconds(30);

    private readonly IAppSettingsService _settings;
    private readonly SyncPassphraseCache _passphraseCache;
    private readonly SyncCoordinator _coordinator;
    private readonly ILogger<BackgroundSyncService> _logger;

    public BackgroundSyncService(
        IAppSettingsService settings,
        SyncPassphraseCache passphraseCache,
        SyncCoordinator coordinator,
        ILogger<BackgroundSyncService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(passphraseCache);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(logger);
        _settings = settings;
        _passphraseCache = passphraseCache;
        _coordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRunAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var current = _settings.Current;
                var intervalMinutes = current.SyncIntervalMinutes;
                if (current.SyncEnabled
                    && intervalMinutes > 0
                    && DateTimeOffset.UtcNow - lastRunAt >= TimeSpan.FromMinutes(intervalMinutes)
                    && _passphraseCache.TryGet(out var passphrase))
                {
                    try
                    {
                        var result = await _coordinator.SyncAsync(passphrase, stoppingToken).ConfigureAwait(false);
                        lastRunAt = DateTimeOffset.UtcNow;
                        _logger.LogInformation(
                            "Background sync ok: pulled={Pulled} pushed={Pushed} auto={Auto} manual={Manual}",
                            result.PulledCount, result.PushedCount,
                            result.AutoResolvedConflicts, result.ManualConflicts);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Background sync failed; will retry on next tick.");
                        lastRunAt = DateTimeOffset.UtcNow; // back off one interval after failure
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Background sync loop error");
            }

            try { await Task.Delay(PollGranularity, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
