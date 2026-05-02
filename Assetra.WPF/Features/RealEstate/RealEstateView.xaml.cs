using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.RealEstate;

public partial class RealEstateView : UserControl
{
    public RealEstateView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>
    /// Triggers the ViewModel's LoadAsync command the first time the view becomes
    /// visible after the app starts.  The hub views are hosted in a TabControl and
    /// the singleton ViewModel is constructed at app startup, so without this hook
    /// the persisted rows in SQLite are never read into the ObservableCollection.
    /// </summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is RealEstateViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
