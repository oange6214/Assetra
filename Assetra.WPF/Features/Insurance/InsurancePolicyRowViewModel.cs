using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Insurance;

public sealed partial class InsurancePolicyRowViewModel : ObservableObject
{
    public Guid Id { get; }
    public string Name { get; }
    public string PolicyNumber { get; }
    public InsuranceType Type { get; }
    public string Insurer { get; }
    public DateOnly StartDate { get; }
    public DateOnly? MaturityDate { get; }
    public decimal FaceValue { get; }
    public decimal CashValue { get; }
    public decimal AnnualPremium { get; }
    public decimal TotalPremiumsPaid { get; }
    public string Currency { get; }
    public InsurancePolicyStatus Status { get; }
    public string? Notes { get; }

    public InsurancePolicyRowViewModel(InsuranceCashValueSummary summary)
    {
        var p = summary.Policy;
        Id = p.Id;
        Name = p.Name;
        PolicyNumber = p.PolicyNumber;
        Type = p.Type;
        Insurer = p.Insurer;
        StartDate = p.StartDate;
        MaturityDate = p.MaturityDate;
        FaceValue = p.FaceValue;
        CashValue = summary.CashValue;
        AnnualPremium = p.AnnualPremium;
        TotalPremiumsPaid = summary.TotalPremiumsPaid;
        Currency = p.Currency;
        Status = p.Status;
        Notes = p.Notes;
    }
}
