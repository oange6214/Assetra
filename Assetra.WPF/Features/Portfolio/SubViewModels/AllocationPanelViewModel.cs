using System.Collections.ObjectModel;
using Assetra.Core.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Owns the asset allocation pie-chart slices and LiveCharts series.
/// Extracted from <see cref="PortfolioViewModel"/> to keep the parent VM focused.
/// </summary>
public sealed partial class AllocationPanelViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<AssetType, (string LabelKey, string Color)> AssetTypeColors =
        new Dictionary<AssetType, (string, string)>
        {
            [AssetType.Stock] = ("Portfolio.AssetType.Stock", "#3B82F6"),
            [AssetType.Fund] = ("Portfolio.AssetType.Fund", "#10B981"),
            [AssetType.PreciousMetal] = ("Portfolio.AssetType.Metal", "#F59E0B"),
            [AssetType.Bond] = ("Portfolio.AssetType.Bond", "#6B7280"),
            [AssetType.Crypto] = ("Portfolio.AssetType.Crypto", "#8B5CF6"),
        };

    private readonly ILocalizationService? _localization;

    public AllocationPanelViewModel(ILocalizationService? localization)
    {
        _localization = localization;
    }

    public ObservableCollection<AssetAllocationSlice> Slices { get; } = [];

    [ObservableProperty] private ISeries[] _pieSeries = [];

    public bool HasData => Slices.Count > 0;

    /// <summary>
    /// Reconciles <see cref="Slices"/> and <see cref="PieSeries"/> from the latest
    /// <see cref="AllocationSliceResult"/> set produced by the summary service.
    /// </summary>
    public void Apply(IReadOnlyList<AllocationSliceResult> slices)
    {
        var newSlices = new List<AssetAllocationSlice>();

        foreach (var slice in slices)
        {
            switch (slice.Kind)
            {
                case AllocationSliceKind.AssetType when slice.AssetType is AssetType assetType:
                    if (!AssetTypeColors.TryGetValue(assetType, out var meta))
                        continue;
                    var label = L(meta.LabelKey, assetType.ToString());
                    newSlices.Add(new AssetAllocationSlice(label, slice.Value, slice.Percent, meta.Color));
                    break;
                case AllocationSliceKind.Cash:
                    newSlices.Add(new AssetAllocationSlice(
                        L("Portfolio.Header.Cash", "Cash"),
                        slice.Value,
                        slice.Percent,
                        "#94A3B8"));
                    break;
                case AllocationSliceKind.Liabilities:
                    newSlices.Add(new AssetAllocationSlice(
                        L("Portfolio.Header.Liabilities", "Liabilities"),
                        slice.Value,
                        slice.Percent,
                        "#EF4444"));
                    break;
            }
        }

        // Dirty-check: only rebuild PieSeries when slice data has materially changed.
        // LiveCharts treats a new ISeries[] reference as a full chart reset (animation flicker + GC).
        // Comparing by label + value (rounded to nearest integer) avoids churn on tiny price ticks.
        bool slicesChanged = newSlices.Count != Slices.Count;
        if (!slicesChanged)
        {
            for (var i = 0; i < newSlices.Count; i++)
            {
                var n = newSlices[i];
                var o = Slices[i];
                if (n.Label != o.Label || Math.Round(n.Value) != Math.Round(o.Value))
                {
                    slicesChanged = true;
                    break;
                }
            }
        }

        if (!slicesChanged)
            return;

        Slices.Clear();
        foreach (var s in newSlices)
            Slices.Add(s);

        if (newSlices.Count == 0)
        {
            PieSeries = [];
            OnPropertyChanged(nameof(HasData));
            return;
        }

        // Build PieSeries for LiveChartsCore v2 (must use double, not decimal).
        PieSeries = Slices
            .Select(s =>
            {
                var paint = new SolidColorPaint(SKColor.Parse(s.ColorHex));
                return (ISeries)new PieSeries<double>
                {
                    Values = new[] { (double)s.Value },
                    // Name drives the LiveChartsCore tooltip (built-in legend is hidden).
                    Name = $"{s.Label}  NT${s.Value:N0}  ({s.Percent:F1}%)",
                    InnerRadius = 40,
                    Fill = paint,
                    Stroke = null,
                    DataLabelsSize = 0,
                    HoverPushout = 6,
                    AnimationsSpeed = TimeSpan.Zero,
                };
            })
            .ToArray();

        OnPropertyChanged(nameof(HasData));
    }

    private string L(string key, string fallback = "") =>
        _localization?.Get(key, fallback) ?? fallback;
}
