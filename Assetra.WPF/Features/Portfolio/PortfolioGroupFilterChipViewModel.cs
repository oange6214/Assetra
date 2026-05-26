using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio;

public sealed partial class PortfolioGroupFilterChipViewModel : ObservableObject
{
    public PortfolioGroupFilterChipViewModel(Guid? id, string name, bool isSystem)
    {
        Id = id;
        Name = name;
        IsSystem = isSystem;
    }

    public Guid? Id { get; }
    public string Name { get; }
    public bool IsSystem { get; }

    [ObservableProperty] private bool _isSelected;
}
