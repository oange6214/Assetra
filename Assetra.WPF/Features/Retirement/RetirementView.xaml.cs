using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Retirement;

public partial class RetirementView : UserControl
{
    public RetirementView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is RetirementViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
