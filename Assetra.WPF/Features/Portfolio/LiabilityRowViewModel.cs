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

    public ObservableCollection<LoanScheduleRowViewModel> ScheduleEntries { get; } = [];

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
        Label = label;
        _balance = snapshot.Balance;
        _originalAmount = snapshot.OriginalAmount;

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
