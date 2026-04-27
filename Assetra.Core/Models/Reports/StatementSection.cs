namespace Assetra.Core.Models.Reports;

/// <summary>
/// 報表中的一個分節（例：「收入」「支出」「資產」「負債」），含多列與小計。
/// </summary>
public sealed record StatementSection(
    string Title,
    IReadOnlyList<StatementRow> Rows,
    decimal Total);
