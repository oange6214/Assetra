using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.FinancialOverview;

/// <summary>
/// One checkbox row inside the KPI selector dialog. The IsSelected change
/// callback raises a delegate up to the host ViewModel so it can recompute
/// "is at least 3 selected" / "is at most 6 selected" gating without each
/// item needing a back-reference to the host.
/// </summary>
public sealed partial class KpiSelectionItemVm : ObservableObject
{
    private readonly Action _onChanged;

    public KpiSelectionItemVm(KpiMetricInfo info, bool isSelected, Action onChanged)
    {
        Id = info.Id;
        LabelKey = info.LabelKey;
        DescriptionKey = info.DescriptionKey;
        _isSelected = isSelected;
        _onChanged = onChanged;
    }

    public KpiMetric Id { get; }
    public string LabelKey { get; }
    public string DescriptionKey { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onChanged();
}
