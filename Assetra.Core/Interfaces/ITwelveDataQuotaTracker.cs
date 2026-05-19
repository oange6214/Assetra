namespace Assetra.Core.Interfaces;

public interface ITwelveDataQuotaTracker
{
    int DailyLimit { get; }
    int UsedToday { get; }
    string UsageDate { get; }
    Task RecordAsync(int credits, CancellationToken ct = default);
}
