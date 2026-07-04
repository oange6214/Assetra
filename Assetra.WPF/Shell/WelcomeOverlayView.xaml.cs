using System.Windows.Controls;

namespace Assetra.WPF.Shell;

/// <summary>
/// First-run welcome overlay. Visual only — all state lives on the
/// <see cref="WelcomeGateViewModel"/> the shell points its DataContext at.
/// </summary>
public partial class WelcomeOverlayView : UserControl
{
    public WelcomeOverlayView()
    {
        InitializeComponent();
    }
}
