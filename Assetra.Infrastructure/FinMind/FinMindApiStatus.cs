namespace Assetra.Infrastructure.FinMind;

/// <summary>
/// Shared availability flag for all FinMind API consumers.
/// When any provider (history or institutional) detects a quota or auth error,
/// it marks the entire FinMind API as unavailable for the current session.
/// </summary>
public sealed class FinMindApiStatus
{
    private volatile bool _isAvailable = true;

    public bool IsAvailable => _isAvailable;

    public void MarkUnavailable() => _isAvailable = false;

    /// <summary>
    /// Resets availability to true — call after updating the API token so the
    /// service retries requests that previously failed due to an invalid token.
    /// </summary>
    public void Reset() => _isAvailable = true;
}
