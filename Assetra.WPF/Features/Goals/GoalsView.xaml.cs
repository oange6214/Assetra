using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Goals;

public partial class GoalsView : UserControl
{
    public GoalsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is GoalsViewModel vm && !vm.IsLoaded && !vm.IsLoading)
            vm.LoadCommand.Execute(null);
    }
}
