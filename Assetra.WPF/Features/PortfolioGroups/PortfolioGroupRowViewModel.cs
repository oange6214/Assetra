using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.PortfolioGroups;

/// <summary>
/// 單一群組 row，binding to PortfolioGroupsView's ItemsControl. Wraps the
/// immutable <see cref="PortfolioGroup"/> record so editing in-place re-fires
/// PropertyChanged for any computed display property.
/// </summary>
public sealed partial class PortfolioGroupRowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Id))]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(ColorHex))]
    [NotifyPropertyChangedFor(nameof(EffectiveColorHex))]
    [NotifyPropertyChangedFor(nameof(IsSystem))]
    [NotifyPropertyChangedFor(nameof(SortOrder))]
    private PortfolioGroup _group;

    public PortfolioGroupRowViewModel(PortfolioGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        _group = group;
    }

    public Guid Id => Group.Id;
    public string Name => Group.Name;
    public string? Description => Group.Description;
    public string? ColorHex => Group.ColorHex;
    public bool IsSystem => Group.IsSystem;
    public int SortOrder => Group.SortOrder;

    /// <summary>UI fallback color when user hasn't picked one — accent tone.</summary>
    public string EffectiveColorHex =>
        string.IsNullOrWhiteSpace(Group.ColorHex) ? "#3B82F6" : Group.ColorHex!;
}
