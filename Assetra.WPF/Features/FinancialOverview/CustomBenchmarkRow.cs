namespace Assetra.WPF.Features.FinancialOverview;

/// <summary>
/// v2：使用者自訂對標 — 顯示在「資產趨勢」對標比較區與內建 4 個並列。
/// 由 PortfolioHistoryViewModel.UpdateBenchmarksAsync 構建；symbol 取自
/// AppSettings.CustomBenchmarkSymbols。
/// </summary>
public sealed record CustomBenchmarkRow(string Symbol, string Display);
