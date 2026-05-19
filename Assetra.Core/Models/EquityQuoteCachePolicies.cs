namespace Assetra.Core.Models;

public static class EquityQuoteCachePolicies
{
    public static readonly TimeSpan Fresh = TimeSpan.Zero;
    public static readonly TimeSpan ManualRefresh = TimeSpan.Zero;
    public static readonly TimeSpan SchedulerRefresh = TimeSpan.Zero;
    public static readonly TimeSpan DashboardAutoRedraw = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan AlertEvaluation = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan HoverPreview = TimeSpan.FromSeconds(120);
}
