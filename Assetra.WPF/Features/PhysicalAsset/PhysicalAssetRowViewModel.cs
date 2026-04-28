using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.PhysicalAsset;

public sealed partial class PhysicalAssetRowViewModel : ObservableObject
{
    public Guid Id { get; }
    public string Name { get; }
    public PhysicalAssetCategory Category { get; }
    public string Description { get; }
    public decimal AcquisitionCost { get; }
    public DateOnly AcquisitionDate { get; }
    public decimal CurrentValue { get; }
    public string ValuationMethod { get; }
    public string Currency { get; }
    public PhysicalAssetStatus Status { get; }
    public string? Notes { get; }
    public decimal UnrealizedGain { get; }
    public decimal UnrealizedGainRate { get; }

    public PhysicalAssetRowViewModel(PhysicalAssetSummary summary)
    {
        var a = summary.Asset;
        Id = a.Id;
        Name = a.Name;
        Category = a.Category;
        Description = a.Description;
        AcquisitionCost = a.AcquisitionCost;
        AcquisitionDate = a.AcquisitionDate;
        CurrentValue = a.CurrentValue;
        ValuationMethod = a.ValuationMethod;
        Currency = a.Currency;
        Status = a.Status;
        Notes = a.Notes;
        UnrealizedGain = summary.UnrealizedGain;
        UnrealizedGainRate = summary.UnrealizedGainRate;
    }
}
