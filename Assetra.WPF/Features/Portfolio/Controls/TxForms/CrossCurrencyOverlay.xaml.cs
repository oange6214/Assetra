using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Assetra.WPF.Features.Portfolio.Controls.TxForms;

/// <summary>
/// P5.8b — Cross-currency settlement section 共用 UserControl。
/// 三個 transaction form (Buy / Sell / CashDividend) 過去各自重複實作這段 UI；
/// 抽出來後三邊只剩一行 markup + 設定 4 個 DP。
///
/// DataContext 期待是 <c>BuyTxViewModel</c> / <c>SellTxViewModel</c> /
/// <c>DividendTxViewModel</c>，這三個 sub-VM 在 P5.8a 已對齊 settlement
/// 屬性介面（SettlementInputMode / IsCrossCurrency / SettlementPairDisplay /
/// SettlementCurrencyDisplay / ActualCashAmount / ActualCashAmountError /
/// IsStatementSettlementMode / IsFxSettlementMode / FxRate / FxRateError /
/// FxRateDateDisplay / FxFetchError / FxSourceDisplay）。
///
/// 呼叫端 form XAML 範例：
/// <code>
/// &lt;txforms:CrossCurrencyOverlay
///     DataContext="{Binding Buy}"
///     GroupName="TxBuySettlementInputMode"
///     FetchFxRateCommand="{Binding DataContext.FetchBuyFxRateCommand,
///         RelativeSource={RelativeSource AncestorType=Window}}"
///     ActualCashAmountLabel="{DynamicResource Portfolio.Tx.ActualCashAmount}"
///     ActualCashAmountHint="{DynamicResource Portfolio.Tx.ActualCashAmountHint}" /&gt;
/// </code>
/// </summary>
public partial class CrossCurrencyOverlay : UserControl
{
    /// <summary>
    /// RadioButton GroupName — 不同 form 必須給不同值避免 cross-form RadioButton 串組。
    /// Convention：<c>TxBuySettlementInputMode</c> / <c>TxSellSettlementInputMode</c> /
    /// <c>TxDivSettlementInputMode</c>。
    /// </summary>
    public static readonly DependencyProperty GroupNameProperty = DependencyProperty.Register(
        nameof(GroupName),
        typeof(string),
        typeof(CrossCurrencyOverlay),
        new PropertyMetadata(string.Empty));

    public string GroupName
    {
        get => (string)GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    /// <summary>
    /// 父 VM 暴露的「一鍵抓匯率」command。三個 form 各自的
    /// <c>FetchBuyFxRateCommand</c> / <c>FetchSellFxRateCommand</c> /
    /// <c>FetchDividendFxRateCommand</c>。
    /// </summary>
    public static readonly DependencyProperty FetchFxRateCommandProperty = DependencyProperty.Register(
        nameof(FetchFxRateCommand),
        typeof(ICommand),
        typeof(CrossCurrencyOverlay),
        new PropertyMetadata(null));

    public ICommand? FetchFxRateCommand
    {
        get => (ICommand?)GetValue(FetchFxRateCommandProperty);
        set => SetValue(FetchFxRateCommandProperty, value);
    }

    /// <summary>
    /// 「實際扣款金額 / 入帳金額」label 文字。語意因 tx type 而異：
    /// Buy = 實際扣款金額 / Sell = 實際入帳金額 / Div = 實際入帳金額。
    /// 用 DynamicResource 帶入對應 lang key 字串。
    /// </summary>
    public static readonly DependencyProperty ActualCashAmountLabelProperty = DependencyProperty.Register(
        nameof(ActualCashAmountLabel),
        typeof(string),
        typeof(CrossCurrencyOverlay),
        new PropertyMetadata(string.Empty));

    public string ActualCashAmountLabel
    {
        get => (string)GetValue(ActualCashAmountLabelProperty);
        set => SetValue(ActualCashAmountLabelProperty, value);
    }

    /// <summary>「實際扣款/入帳金額」label 下方的 hint 文字。</summary>
    public static readonly DependencyProperty ActualCashAmountHintProperty = DependencyProperty.Register(
        nameof(ActualCashAmountHint),
        typeof(string),
        typeof(CrossCurrencyOverlay),
        new PropertyMetadata(string.Empty));

    public string ActualCashAmountHint
    {
        get => (string)GetValue(ActualCashAmountHintProperty);
        set => SetValue(ActualCashAmountHintProperty, value);
    }

    public CrossCurrencyOverlay()
    {
        InitializeComponent();
    }
}
