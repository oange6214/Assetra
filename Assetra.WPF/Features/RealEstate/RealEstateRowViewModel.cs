using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.RealEstate;

public sealed partial class RealEstateRowViewModel : ObservableObject
{
    public Guid Id { get; }
    public string Name { get; }
    public string Address { get; }
    public decimal PurchasePrice { get; }
    public DateOnly PurchaseDate { get; }
    public decimal CurrentValue { get; }
    public decimal MortgageBalance { get; }
    public decimal Equity { get; }
    public string Currency { get; }
    public bool IsRental { get; }
    public RealEstateStatus Status { get; }
    public string? Notes { get; }
    public decimal MonthlyNetRental { get; }

    public RealEstateRowViewModel(RealEstateValuationSummary summary)
    {
        var p = summary.Property;
        Id = p.Id;
        Name = p.Name;
        Address = p.Address;
        PurchasePrice = p.PurchasePrice;
        PurchaseDate = p.PurchaseDate;
        CurrentValue = p.CurrentValue;
        MortgageBalance = p.MortgageBalance;
        Equity = p.Equity;
        Currency = p.Currency;
        IsRental = p.IsRental;
        Status = p.Status;
        Notes = p.Notes;
        MonthlyNetRental = summary.MonthlyNetRental;
    }
}
