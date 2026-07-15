using System.Windows;
using System.Windows.Controls;

namespace Assetra.WPF.Features.Portfolio.Controls.TxForms.Shared;

/// <summary>
/// Wave 1 — 抽出 transaction form 反覆手刻的「semibold label → TextBox → FormFieldError」
/// 三件組。第一次只套用在 BuyTxForm 的「數量」欄位，行為與原本手刻 markup 完全一致。
///
/// 內層 TextBox 沿用設計系統的隱式 TextBox 樣式（不指定 Style）、用 <c>Tag</c> 當
/// placeholder、錯誤文字走 <c>FormFieldError</c> 樣式 + <c>StringToVisibilityConverter</c>。
///
/// <para>
/// <b>ThousandSeparator</b> 不在原始需求 DP 清單內，但「數量」欄位原本掛了
/// <see cref="Infrastructure.Behaviors.ThousandSeparatorBehavior"/>，若省略會改變輸入行為。
/// 為了同時保住「行為零變化」與「FormField 可重用於一般文字欄位」，改用一個預設
/// <c>false</c> 的 bool DP 由呼叫端逐欄開啟，而非在樣板內硬寫 <c>True</c>。
/// </para>
/// </summary>
public partial class FormField : UserControl
{
    /// <summary>欄位標題文字（semibold label）。呼叫端一般用 DynamicResource 帶語系字串。</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(FormField),
        new PropertyMetadata(string.Empty));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// 輸入框文字。預設雙向：內層 TextBox 以 PropertyChanged 回寫此 DP，此 DP 再回寫
    /// 呼叫端的 VM 屬性，維持原本「TextBox 直接綁 VM」的同步時機。
    /// </summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(FormField),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>錯誤文字。非空字串時顯示錯誤 TextBlock（走 StringToVisibilityConverter）。</summary>
    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText),
        typeof(string),
        typeof(FormField),
        new PropertyMetadata(string.Empty));

    public string ErrorText
    {
        get => (string)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    /// <summary>輸入框 placeholder（掛在 TextBox.Tag，由隱式樣式的浮水印呈現）。</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(FormField),
        new PropertyMetadata(string.Empty));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>輸入框是否可編輯（對應原本的 IsEnabled 綁定）。</summary>
    public static readonly DependencyProperty InputEnabledProperty = DependencyProperty.Register(
        nameof(InputEnabled),
        typeof(bool),
        typeof(FormField),
        new PropertyMetadata(true));

    public bool InputEnabled
    {
        get => (bool)GetValue(InputEnabledProperty);
        set => SetValue(InputEnabledProperty, value);
    }

    /// <summary>
    /// 是否對輸入框啟用千分位格式化行為（<see cref="Infrastructure.Behaviors.ThousandSeparatorBehavior"/>）。
    /// 預設 false（一般文字欄位）；數值欄位（如數量）由呼叫端設 True 保住原行為。
    /// </summary>
    public static readonly DependencyProperty ThousandSeparatorProperty = DependencyProperty.Register(
        nameof(ThousandSeparator),
        typeof(bool),
        typeof(FormField),
        new PropertyMetadata(false));

    public bool ThousandSeparator
    {
        get => (bool)GetValue(ThousandSeparatorProperty);
        set => SetValue(ThousandSeparatorProperty, value);
    }

    /// <summary>
    /// 錯誤文字 TextBlock 的 Margin。預設 <c>0,2,0,0</c>，等同 <c>FormFieldError</c> 樣式原本的
    /// Margin，所以未指定此 DP 的呼叫端渲染與過去完全一致。部分表單的錯誤標籤原本掛了局部
    /// <c>Margin="0,0,0,12"</c>（覆寫樣式）以撐出欄位間距，改用此 DP 逐欄帶入即可保住原行為。
    /// </summary>
    public static readonly DependencyProperty ErrorMarginProperty = DependencyProperty.Register(
        nameof(ErrorMargin),
        typeof(Thickness),
        typeof(FormField),
        new PropertyMetadata(new Thickness(0, 2, 0, 0)));

    public Thickness ErrorMargin
    {
        get => (Thickness)GetValue(ErrorMarginProperty);
        set => SetValue(ErrorMarginProperty, value);
    }

    /// <summary>
    /// 選填的欄位說明。非空字串時在 label 右側顯示一顆 ⓘ，說明只在 hover 時以 tooltip 呈現。
    /// 用來取代原本常駐在欄位下方、把表單撐成「說明書」的 hint TextBlock（文案 key 不變）。
    /// 留空（預設）時完全不顯示圖示，既有呼叫端渲染不受影響。
    /// </summary>
    public static readonly DependencyProperty InfoTooltipProperty = DependencyProperty.Register(
        nameof(InfoTooltip),
        typeof(string),
        typeof(FormField),
        new PropertyMetadata(string.Empty));

    public string InfoTooltip
    {
        get => (string)GetValue(InfoTooltipProperty);
        set => SetValue(InfoTooltipProperty, value);
    }

    public FormField()
    {
        InitializeComponent();
    }
}
