using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.PhysicalAsset;

public partial class PhysicalAssetView : UserControl
{
    public PhysicalAssetView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is PhysicalAssetViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
