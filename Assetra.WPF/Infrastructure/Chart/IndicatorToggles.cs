namespace Assetra.WPF.Infrastructure.Chart;

/// <summary>指標顯示開關狀態，對應 JS Bridge 的 IndicatorToggles 型別。</summary>
public sealed record IndicatorToggles(
    bool BollingerBands,
    bool VolumeMa,
    bool Rsi,
    bool Kd,
    bool Macd);
