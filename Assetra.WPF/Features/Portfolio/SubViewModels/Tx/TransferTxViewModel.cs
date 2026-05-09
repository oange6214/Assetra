using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels.Tx;

/// <summary>
/// H1 — fifth child VM split off from <c>TransactionDialogViewModel</c>.
/// Owns the **transfer transaction state cluster**:
/// <list type="bullet">
///   <item><see cref="Target"/>: target cash account picker (CashAccountRowViewModel)</item>
///   <item><see cref="TargetName"/>: typed text fallback when picker is null
///         (FindOrCreateAccountAsync runs at confirm time)</item>
///   <item><see cref="TargetAmount"/> + <see cref="TargetAmountError"/>: amount the
///         destination account receives — may differ from source amount for
///         cross-currency transfers</item>
///   <item><see cref="ImpliedRate"/>: source / target ratio for the dialog hint</item>
/// </list>
///
/// <para>
/// Source amount lives on the parent dialog VM as <c>TxAmount</c> since it's shared
/// with other transaction types. ImpliedRate compute pulls source from a callback so
/// this sub-VM stays loosely coupled to the parent.
/// </para>
/// </summary>
public sealed partial class TransferTxViewModel : ObservableObject
{
    /// <summary>Target cash account picker — wins when set.</summary>
    [ObservableProperty] private CashAccountRowViewModel? _target;

    /// <summary>Free-form target name (fallback when picker is null).</summary>
    [ObservableProperty] private string _targetName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImpliedRate))]
    private string _targetAmount = string.Empty;

    [ObservableProperty] private string _targetAmountError = string.Empty;

    /// <summary>
    /// Caller-supplied source amount text. Set by the dialog VM whenever its
    /// shared <c>TxAmount</c> changes so this VM can recompute <see cref="ImpliedRate"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImpliedRate))]
    private string _sourceAmountText = string.Empty;

    /// <summary>
    /// Implied source / target rate for the dialog hint. "—" when either side
    /// is missing or non-positive.
    /// </summary>
    public string ImpliedRate
    {
        get
        {
            if (!ParseHelpers.TryParseDecimal(SourceAmountText, out var src) || src <= 0)
                return "—";
            if (!ParseHelpers.TryParseDecimal(TargetAmount, out var dst) || dst <= 0)
                return "—";
            return (src / dst).ToString("F4");
        }
    }

    public void Reset()
    {
        Target = null;
        TargetName = string.Empty;
        TargetAmount = string.Empty;
        TargetAmountError = string.Empty;
        SourceAmountText = string.Empty;
    }
}
