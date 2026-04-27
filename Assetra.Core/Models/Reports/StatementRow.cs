namespace Assetra.Core.Models.Reports;

/// <summary>
/// 報表中的一列。<paramref name="Group"/> 用於子分組（例如「住房 → 房租」）；頂層為 null。
/// </summary>
public sealed record StatementRow(string Label, decimal Amount, string? Group = null);
