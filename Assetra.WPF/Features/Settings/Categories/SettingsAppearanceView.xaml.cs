using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Assetra.WPF.Features.Settings.Categories;

public partial class SettingsAppearanceView : UserControl
{
    public SettingsAppearanceView() => InitializeComponent();

    private async void OnUiScaleSliderDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.CommitUiScaleAsync();
    }

    private async void OnUiScaleSliderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.CommitUiScaleAsync();
    }
}
