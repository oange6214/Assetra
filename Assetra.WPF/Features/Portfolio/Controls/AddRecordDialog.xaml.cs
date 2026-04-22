using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Portfolio.Controls;

public partial class AddRecordDialog : UserControl
{
    public AddRecordDialog()
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
