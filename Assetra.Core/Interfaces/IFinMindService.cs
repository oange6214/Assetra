namespace Assetra.Core.Interfaces;

public interface IFinMindService
{
    /// <summary>False when the FinMind API has returned a quota / auth error during this session.</summary>
    bool IsAvailable { get; }

    /// <summary>Returns the daily close price for the given symbol and date, or null if unavailable.</summary>
    Task<decimal?> GetDailyCloseAsync(string symbol, DateOnly date, CancellationToken ct = default);
}
