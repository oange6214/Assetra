using System.Windows.Controls;

namespace Assetra.WPF.Features.AuditLog;

/// <summary>
/// Right-pane detail card for the master-detail audit log layout. Renders a
/// parsed Trade snapshot as a humanised field grid + collapsible raw JSON.
/// Bound to <see cref="TradeDetailViewModel"/>.
/// </summary>
public partial class TradeDetailCardView : UserControl
{
    public TradeDetailCardView() => InitializeComponent();
}
