namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// Represents one segment of the asset allocation pie chart.
/// </summary>
/// <param name="Label">Display label (e.g. "股票/ETF", "Cash").</param>
/// <param name="Value">Absolute monetary value.</param>
/// <param name="Percent">Proportion 0–100 of total assets.</param>
/// <param name="ColorHex">Hex color for the chart segment (e.g. "#3B82F6").</param>
public sealed record AssetAllocationSlice(
    string Label,
    decimal Value,
    decimal Percent,
    string ColorHex);
