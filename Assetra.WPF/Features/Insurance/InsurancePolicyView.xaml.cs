using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Insurance;

public partial class InsurancePolicyView : UserControl
{
    public InsurancePolicyView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is InsurancePolicyViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
