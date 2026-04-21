using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class AddTransactionDialog : UserControl
{
    public AddTransactionDialog()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            ContentScrollViewer.ScrollToTop();
    }
}
