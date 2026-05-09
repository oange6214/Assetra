using System.Windows.Controls;

namespace Assetra.WPF.Features.Assistant;

public partial class AssistantView : UserControl
{
    public AssistantView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is AssistantViewModel vm)
                await vm.LoadInsightsAsync();
        };
    }
}
