namespace Assetra.WPF.Infrastructure.Chart;

/// <summary>AI / 手動標記的抽象基底。</summary>
public abstract record ChartAnnotation;

/// <summary>水平價格參考線（dashed）。</summary>
public sealed record PriceLineAnnotation(
    decimal Price,
    string Color,
    string Label) : ChartAnnotation;

/// <summary>K 線上方或下方的箭頭標記。</summary>
public sealed record MarkerAnnotation(
    string Time,          // 'YYYY-MM-DD'
    string Position,      // 'aboveBar' | 'belowBar'
    string Color,
    string Text) : ChartAnnotation;
