using CommunityToolkit.Mvvm.ComponentModel;
using Assetra.Core.Interfaces;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// 貸款列（負債）。Balance / OriginalAmount 皆為 Trade 歷史投影結果，
/// 透過 <see cref="LiabilityRowViewModel(string, LiabilitySnapshot)"/> 於載入時注入。
/// 不再依賴 AssetItem 實體 — 標識符為貸款名稱字串。
/// </summary>
public sealed partial class LiabilityRowViewModel : ObservableObject
{
    public string Label { get; }

    /// <summary>Display name — equals <see cref="Label"/> (the loan name is the identifier).</summary>
    public string Name => Label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaidPercent))]
    [NotifyPropertyChangedFor(nameof(PaidPercentDisplay))]
    private decimal _balance;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaidPercent))]
    [NotifyPropertyChangedFor(nameof(PaidPercentDisplay))]
    private decimal _originalAmount;

    public double PaidPercent => OriginalAmount > 0
        ? (double)System.Math.Clamp((OriginalAmount - Balance) / OriginalAmount * 100, 0, 100)
        : 0;

    public string PaidPercentDisplay => OriginalAmount > 0
        ? $"{PaidPercent:F0}%"
        : "—";

    public void NotifyCurrencyChanged() => OnPropertyChanged(nameof(Balance));

    public LiabilityRowViewModel(string label, LiabilitySnapshot snapshot)
    {
        Label = label;
        _balance = snapshot.Balance;
        _originalAmount = snapshot.OriginalAmount;
    }

    public override string ToString() => Label;
}
