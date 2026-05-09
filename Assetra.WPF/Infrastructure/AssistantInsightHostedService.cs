using Assetra.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Assetra.WPF.Infrastructure;

/// <summary>
/// AI Phase 2 scheduler — periodically polls <see cref="IAssistantInsightService"/>
/// and pushes Critical / Warning insights to the snackbar so the user notices
/// budget overspending or upcoming subscriptions without having to navigate
/// to the Assistant page.
///
/// <para>
/// Cadence: 4 hours. The service is intentionally chatty-light — only
/// previously-unseen Critical/Warning items trigger a notification.
/// Info-level insights stay confined to the Assistant page.
/// </para>
///
/// <para>
/// Notification de-dup window is the lifetime of the host (process); a future
/// improvement is to persist the last-shown timestamp per insight key in
/// AppSettings or a sidecar SQLite row so a restart doesn't re-spam.
/// </para>
/// </summary>
internal sealed class AssistantInsightHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ISnackbarService? _snackbar;
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly HashSet<string> _alreadyShown = new(StringComparer.Ordinal);

    public AssistantInsightHostedService(IServiceProvider sp, ISnackbarService? snackbar = null)
    {
        _sp = sp;
        _snackbar = snackbar;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't fire immediately on startup — give the rest of the app time
        // to load data so insights have something to look at.
        try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                // Insight loading is best-effort — never crash the host.
            }

            try { await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var insights = _sp.GetService<IAssistantInsightService>();
        if (insights is null || _snackbar is null) return;

        var snapshot = await insights.GetCurrentInsightsAsync(ct).ConfigureAwait(false);
        foreach (var insight in snapshot)
        {
            if (insight.Severity == AssistantInsightSeverity.Info) continue;
            var key = $"{insight.Source}:{insight.Title}";
            if (!_alreadyShown.Add(key)) continue;
            switch (insight.Severity)
            {
                case AssistantInsightSeverity.Critical:
                    _snackbar.Error(insight.Title);
                    break;
                case AssistantInsightSeverity.Warning:
                    _snackbar.Warning(insight.Title);
                    break;
            }
        }
    }
}
