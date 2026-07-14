using System.Windows.Controls;

namespace Assetra.WPF.Features.Portfolio.Controls.TxForms.Shared;

/// <summary>
/// Wave 1 — 抽出 Buy / Sell 表單重複的「手續費 override」區塊：
/// <see cref="FormField"/>（Fee label＋TextBox＋error）＋ FeeOverrideHint 提示。
///
/// DataContext 由外層 TxForm（<c>TransactionDialogViewModel</c>）繼承，直接綁共用 VM 屬性
/// <c>TxFee</c> / <c>TxFeeError</c>，因此本控制項不需任何 DependencyProperty。
///
/// 只有 Buy / Sell 兩張表單帶 override 提示；其餘手續費表單（Income / CashFlow /
/// CashDividend / Loan / Transfer）無 hint、error 間距不同，屬另一種區塊，不套用此控制項。
/// </summary>
public partial class TxFeeField : UserControl
{
    public TxFeeField()
    {
        InitializeComponent();
    }
}
