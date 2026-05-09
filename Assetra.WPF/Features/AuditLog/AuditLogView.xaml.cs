using System.Windows.Controls;

namespace Assetra.WPF.Features.AuditLog;

public partial class AuditLogView : UserControl
{
    public AuditLogView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is AuditLogViewModel vm)
                await vm.LoadAsync();
        };
    }
}
