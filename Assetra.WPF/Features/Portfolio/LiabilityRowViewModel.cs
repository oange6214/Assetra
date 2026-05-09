using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// 負債列（貸款 / 信用卡）。Balance / OriginalAmount 皆為 Trade 歷史投影結果，
/// 透過 <see cref="LiabilityRowViewModel(string, LiabilitySnapshot, AssetItem?)"/> 於載入時注入。
/// 若存在對應的 <see cref="AssetItem"/>，則同時包含貸款或信用卡附加資訊。
/// </summary>
public sealed partial class LiabilityRowViewModel : ObservableObject
{
    public string Label { get; }

    /// <summary>Display name — equals <see cref="Label"/> (the loan name is the identifier).</summary>
    public string Name => Label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaidPercent))]
    [NotifyPropertyChangedFor(nameof(PaidPercentDisplay))]
    [NotifyPropertyChangedFor(nameof(BalanceAsMoney))]
    private decimal _balance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaidPercent))]
    [NotifyPropertyChangedFor(nameof(PaidPercentDisplay))]
    [NotifyPropertyChangedFor(nameof(OriginalAmountAsMoney))]
    private decimal _originalAmount;

    /// <summary>
    /// M1 — currency-tagged accessor (uses linked AssetItem.Currency, defaults
    /// to "TWD" for legacy labels with no AssetItem). For aggregation that needs
    /// to respect currency boundaries.
    /// </summary>
    public Money BalanceAsMoney => new(Balance, _currency);
    public Money OriginalAmountAsMoney => new(OriginalAmount, _currency);

    private readonly string _currency;

    public double PaidPercent => OriginalAmount > 0
        ? (double)System.Math.Clamp((OriginalAmount - Balance) / OriginalAmount * 100, 0, 100)
        : 0;

    public string PaidPercentDisplay => OriginalAmount > 0
        ? $"{PaidPercent:F0}%"
        : "—";

    // ── Liability metadata (null when no AssetItem linked) ────────────────

    public Guid? AssetId { get; }
    public bool IsLoan { get; }
    public bool IsCreditCard { get; }
    public decimal? LoanAnnualRate { get; }
    public int? LoanTermMonths { get; }
    public DateOnly? LoanStartDate { get; }
    public decimal? LoanHandlingFee { get; }
    public int? BillingDay { get; }
    public int? DueDay { get; }
    public decimal? CreditLimit { get; }
    public string? IssuerName { get; }

    public string RateDisplay => LoanAnnualRate.HasValue ? $"{LoanAnnualRate.Value * 100:F2}%" : "—";
    public string TermDisplay => LoanTermMonths.HasValue ? $"{LoanTermMonths.Value} 個月" : "—";

    // ── Amortization schedule (loaded lazily after selection) ─────────────

    private readonly ObservableCollection<LoanScheduleRowViewModel> _scheduleEntries = [];
    public ReadOnlyObservableCollection<LoanScheduleRowViewModel> ScheduleEntries { get; }

    /// <summary>
    /// LoanDialog 重算攤還表後，由它呼叫此方法刷新 schedule。
    /// 內部封裝避免外部直接 mutate <see cref="_scheduleEntries"/>。
    /// </summary>
    public void ReplaceSchedule(IEnumerable<LoanScheduleRowViewModel> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _scheduleEntries.Clear();
        foreach (var e in entries)
            _scheduleEntries.Add(e);
        OnPropertyChanged(nameof(PaidPrincipal));
        OnPropertyChanged(nameof(PaidInterest));
        OnPropertyChanged(nameof(RemainingFromSchedule));
        OnPropertyChanged(nameof(HasSchedule));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaidPrincipal))]
    [NotifyPropertyChangedFor(nameof(PaidInterest))]
    [NotifyPropertyChangedFor(nameof(RemainingFromSchedule))]
    [NotifyPropertyChangedFor(nameof(HasSchedule))]
    private bool _isScheduleLoaded;

    public bool HasSchedule => IsScheduleLoaded && ScheduleEntries.Count > 0;

    public decimal PaidPrincipal =>
        ScheduleEntries.Where(e => e.IsPaid).Sum(e => e.PrincipalAmount);

    public decimal PaidInterest =>
        ScheduleEntries.Where(e => e.IsPaid).Sum(e => e.InterestAmount);

    public decimal RemainingFromSchedule =>
        ScheduleEntries.Where(e => !e.IsPaid).Sum(e => e.PrincipalAmount);

    public LoanScheduleRowViewModel? NextUnpaidEntry =>
        ScheduleEntries.FirstOrDefault(e => !e.IsPaid);

    public void NotifyCurrencyChanged() => OnPropertyChanged(nameof(Balance));

    public void RefreshScheduleSummary()
    {
        OnPropertyChanged(nameof(PaidPrincipal));
        OnPropertyChanged(nameof(PaidInterest));
        OnPropertyChanged(nameof(RemainingFromSchedule));
        OnPropertyChanged(nameof(NextUnpaidEntry));
    }

    public LiabilityRowViewModel(string label, LiabilitySnapshot snapshot, AssetItem? asset = null)
    {
        ScheduleEntries = new ReadOnlyObservableCollection<LoanScheduleRowViewModel>(_scheduleEntries);
        Label = label;
        // M1 — LiabilitySnapshot now carries Money; row VM keeps decimal display fields
        // since UI bindings format with the asset's Currency separately via MoneyFormatter.
        _balance = snapshot.Balance.Amount;
        _originalAmount = snapshot.OriginalAmount.Amount;
        // Capture currency from the linked asset (or snapshot fallback for legacy labels).
        _currency = !string.IsNullOrWhiteSpace(asset?.Currency)
            ? asset!.Currency
            : (string.IsNullOrWhiteSpace(snapshot.Balance.Currency) ? "TWD" : snapshot.Balance.Currency);

        if (asset is not null)
        {
            AssetId = asset.Id;
            IsLoan = asset.IsLoan;
            IsCreditCard = asset.IsCreditCard;
            LoanAnnualRate = asset.LoanAnnualRate;
            LoanTermMonths = asset.LoanTermMonths;
            LoanStartDate = asset.LoanStartDate;
            LoanHandlingFee = asset.LoanHandlingFee;
            BillingDay = asset.BillingDay;
            DueDay = asset.DueDay;
            CreditLimit = asset.CreditLimit;
            IssuerName = asset.IssuerName;
        }
    }

    public override string ToString() => Label;
}
