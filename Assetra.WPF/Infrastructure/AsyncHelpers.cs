using Microsoft.Extensions.Logging;
using Serilog;

namespace Assetra.WPF.Infrastructure;

/// <summary>
///     Helpers for fire-and-forget async patterns.
/// </summary>
/// <remarks>
///     Replaces raw <c>_ = SomeAsync()</c> patterns that swallowed exceptions
///     silently. The helper logs any exception via Serilog so background
///     failures (refresh / snapshot / autosave) become visible in the log
///     instead of vanishing.
/// </remarks>
public static class AsyncHelpers
{
    /// <summary>
    ///     Run <paramref name="work" /> without awaiting; log any exception
    ///     via Serilog with a contextual <paramref name="operationName" />.
    /// </summary>
    /// <param name="work">The async operation to start.</param>
    /// <param name="operationName">
    ///     Stable name shown in the log entry ("FIRE.RefreshChart",
    ///     "Portfolio.RecordSnapshot", etc.). Surface area for grepping logs.
    /// </param>
    public static void SafeFireAndForget(Func<Task> work, string operationName)
    {
        ArgumentNullException.ThrowIfNull(work);
        _ = RunAsync(work, operationName);
    }

    private static async Task RunAsync(Func<Task> work, string operationName)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a normal shutdown / nav signal, not an error.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Fire-and-forget operation {Operation} failed", operationName);
        }
    }
}
