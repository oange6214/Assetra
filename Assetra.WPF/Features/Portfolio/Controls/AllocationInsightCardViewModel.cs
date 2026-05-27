using System.Windows.Media;

namespace Assetra.WPF.Features.Portfolio.Controls;

/// <summary>High-level allocation insight shown beside the detailed allocation table.</summary>
public sealed class AllocationInsightCardViewModel
{
    public AllocationInsightCardViewModel(
        string label,
        string primary,
        string metric,
        string secondary,
        SolidColorBrush accentBrush)
    {
        Label = label;
        Primary = primary;
        Metric = metric;
        Secondary = secondary;
        AccentBrush = accentBrush;
    }

    public string Label { get; }
    public string Primary { get; }
    public string Metric { get; }
    public string Secondary { get; }
    public SolidColorBrush AccentBrush { get; }
}
