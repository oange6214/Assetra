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
///   <item><see cref="SourceCurrency"/> / <see cref="TargetCurrency"/>: 來源 / 目標帳戶幣別，
///         供畫面顯示與「同幣別自動帶入」判斷</item>
///   <item><see cref="ImpliedRate"/>: source / target ratio for the dialog hint</item>
/// </list>
///
/// <para>
/// Source amount lives on the parent dialog VM as <c>TxAmount</c> since it's shared
/// with other transaction types. ImpliedRate compute pulls source from a callback so
/// this sub-VM stays loosely coupled to the parent.
/// </para>
///
/// <para>
/// 同幣別自動帶入：在「已選定目標帳戶」且來源與目標同幣別時，<see cref="TargetAmount"/> 自動鏡射轉出金額
/// （省去重複輸入）；尚未選定目標帳戶時不臆測幣別、維持留白。欄位仍可手動改——一旦使用者把轉入金額改成
/// 跟轉出不同的值，之後再改轉出就不會覆蓋他的輸入。
/// 判斷採無狀態方式（「轉入金額目前是否仍等於轉出金額」），毋須額外旗標，也不會被 Reset 干擾。
/// 跨幣別時完全不自動帶入，實收金額由匯率決定，交由使用者填寫。
/// </para>
/// </summary>
public sealed partial class TransferTxViewModel : ObservableObject
{
    /// <summary>Target cash account picker — wins when set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetCurrency))]
    private CashAccountRowViewModel? _target;

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
    /// 來源帳戶幣別，由 dialog VM 在 <c>TxCashAccount</c> 改變時推入（與 <see cref="SourceAmountText"/>
    /// 相同的單向同步）。供畫面顯示與「同幣別自動帶入」判斷，預設 TWD。
    /// </summary>
    [ObservableProperty] private string _sourceCurrency = "TWD";

    /// <summary>
    /// 目標帳戶幣別；未選帳戶（或僅以文字暫填）時預設 TWD，與 FindOrCreateAccountAsync 的建立預設一致。
    /// </summary>
    public string TargetCurrency =>
        Target?.Currency is { Length: > 0 } currency ? currency : "TWD";

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

    // ── 同幣別自動帶入 ─────────────────────────────────────────────────────────────

    /// <summary>Reset 期間抑制自動帶入，避免清空各欄位時互相觸發。</summary>
    private bool _suspendAutoSync;

    /// <summary>
    /// <see cref="OnSourceAmountTextChanging"/> 時暫存：變更「前」轉入金額是否仍在鏡射轉出
    /// （空、或等於變更前的轉出金額）。只在同一次屬性變更內使用，無須 Reset。
    /// </summary>
    private bool _targetMirroredOldSource;

    /// <summary>來源與目標是否同幣別（大小寫不敏感）。</summary>
    private bool IsSameCurrency =>
        string.Equals(SourceCurrency, TargetCurrency, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否可自動帶入轉入金額：必須「已選定目標帳戶」（<see cref="Target"/> 非 null）且與來源同幣別。
    /// 尚未選定目標帳戶時不臆測幣別、維持留白，交由使用者輸入。
    /// </summary>
    private bool CanAutoFillTarget => Target is not null && IsSameCurrency;

    partial void OnSourceAmountTextChanging(string _)
    {
        // 變更「前」先記住轉入金額是否仍在跟隨轉出（空、或等於目前也就是舊的轉出金額）。
        // 此刻 SourceAmountText 仍是舊值，等 OnSourceAmountTextChanged 才會是新值。
        _targetMirroredOldSource =
            string.IsNullOrEmpty(TargetAmount) ||
            string.Equals(TargetAmount, SourceAmountText, StringComparison.Ordinal);
    }

    partial void OnSourceAmountTextChanged(string value)
    {
        if (_suspendAutoSync)
            return;

        // 只有在「轉入金額還在跟隨轉出」且「目標帳戶已選且同幣別」時才鏡射；
        // 使用者一旦手動改成不同值即停止跟隨。
        if (_targetMirroredOldSource && CanAutoFillTarget)
            TargetAmount = value;
    }

    partial void OnTargetChanged(CashAccountRowViewModel? value)
    {
        if (_suspendAutoSync)
            return;

        // 選到同幣別目標帳戶時，若轉入金額尚未被改成別的值，就把轉出金額帶過去（處理「先打金額後選帳戶」）。
        if (CanAutoFillTarget &&
            (string.IsNullOrEmpty(TargetAmount) ||
             string.Equals(TargetAmount, SourceAmountText, StringComparison.Ordinal)))
        {
            TargetAmount = SourceAmountText;
        }
    }

    public void Reset()
    {
        _suspendAutoSync = true;
        Target = null;
        TargetName = string.Empty;
        TargetAmount = string.Empty;
        TargetAmountError = string.Empty;
        SourceAmountText = string.Empty;
        SourceCurrency = "TWD";
        _suspendAutoSync = false;
    }
}
