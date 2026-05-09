using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels.Tx;

/// <summary>
/// H1-P9 — small sub-VM for the credit-card transaction selector.
/// Owns just the picker state (<see cref="Card"/>); the available options list
/// is still computed by the dialog VM from its <c>Liabilities</c> collection
/// (cross-cluster query that doesn't fit cleanly inside this VM).
///
/// <para>
/// This sub-VM exists primarily for symmetry with the other Tx clusters
/// (Buy / Sell / Div / Loan / Transfer). Future expansion (e.g. card-specific
/// memo, billing-period override) would slot in here.
/// </para>
/// </summary>
public sealed partial class CreditCardTxViewModel : ObservableObject
{
    /// <summary>The selected credit card (null = no selection / n/a).</summary>
    [ObservableProperty] private LiabilityRowViewModel? _card;

    public void Reset() => Card = null;
}
