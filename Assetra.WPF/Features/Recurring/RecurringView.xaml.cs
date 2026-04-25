using System.Windows.Controls;

namespace Assetra.WPF.Features.Recurring;

public partial class RecurringView : UserControl
{
    public RecurringView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is RecurringViewModel vm && !vm.IsLoaded)
                await vm.LoadAsync();
        };
    }
}
