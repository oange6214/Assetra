using System.Windows;
using System.Windows.Controls;
using FluentCommon = FluentIcons.Common;

namespace Assetra.WPF.DesignSystem.Controls;

/// <summary>
/// Thin wrapper that lets the rest of the app keep its
/// <c>&lt;ds:AppIcon Symbol="Navigation24" /&gt;</c> usage while the actual
/// glyph data comes from Microsoft FluentSystemIcons via the
/// FluentIcons.Wpf package. The package's <c>&lt;fi:FluentIcon /&gt;</c>
/// control is used in the default template; this class only exposes a
/// stable string-based API so view code does not need to import the
/// FluentIcons namespace.
/// </summary>
public sealed class AppIcon : Control
{
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(
            nameof(Symbol),
            typeof(string),
            typeof(AppIcon),
            new FrameworkPropertyMetadata(
                "Info24",
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnIconPropertyChanged));

    public static readonly DependencyProperty FilledProperty =
        DependencyProperty.Register(
            nameof(Filled),
            typeof(bool),
            typeof(AppIcon),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnIconPropertyChanged));

    private static readonly DependencyPropertyKey IconElementPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IconElement),
            typeof(FluentCommon.Icon),
            typeof(AppIcon),
            new FrameworkPropertyMetadata(FluentCommon.Icon.Info));

    /// <summary>The resolved FluentSystemIcons enum value the template binds to.</summary>
    public static readonly DependencyProperty IconElementProperty = IconElementPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IconVariantPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IconVariant),
            typeof(FluentCommon.IconVariant),
            typeof(AppIcon),
            new FrameworkPropertyMetadata(FluentCommon.IconVariant.Regular));

    /// <summary>Regular vs Filled variant, derived from the <see cref="Filled"/> flag.</summary>
    public static readonly DependencyProperty IconVariantProperty = IconVariantPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ResolvedIconSizePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ResolvedIconSize),
            typeof(FluentCommon.IconSize),
            typeof(AppIcon),
            new FrameworkPropertyMetadata(FluentCommon.IconSize.Size16));

    /// <summary>
    /// 依 <see cref="Control.FontSize"/> 映射到最接近的原生 Fluent IconSize 變體
    /// （Size16/Size20/Size24/Size32/Size48）。讓不同尺寸 icon 用各自設計師調好
    /// 的筆畫粗細，而不是把 Size24 用 Viewbox 縮放（會讓 16px 看起來太細、44px
    /// 看起來太粗）。
    /// </summary>
    public static readonly DependencyProperty ResolvedIconSizeProperty = ResolvedIconSizePropertyKey.DependencyProperty;

    static AppIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(typeof(AppIcon)));
        FontSizeProperty.OverrideMetadata(typeof(AppIcon),
            new FrameworkPropertyMetadata(
                16.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnFontSizeChanged));
    }

    private static void OnFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AppIcon icon)
            icon.RefreshResolvedSize();
    }

    public AppIcon()
    {
        Refresh();
        RefreshResolvedSize();
    }

    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public FluentCommon.Icon IconElement => (FluentCommon.Icon)GetValue(IconElementProperty);

    public FluentCommon.IconVariant IconVariant => (FluentCommon.IconVariant)GetValue(IconVariantProperty);

    public FluentCommon.IconSize ResolvedIconSize => (FluentCommon.IconSize)GetValue(ResolvedIconSizeProperty);

    private static void OnIconPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AppIcon icon)
            icon.Refresh();
    }

    private void Refresh()
    {
        SetValue(IconElementPropertyKey, ResolveIcon(Symbol));
        SetValue(IconVariantPropertyKey, Filled
            ? FluentCommon.IconVariant.Filled
            : FluentCommon.IconVariant.Regular);
    }

    /// <summary>
    /// 永遠選 Size24 — Fluent System Icons 的 Size24 變體最完整（所有 icon 都有，
    /// 而 Size16/Size20/Size48 不少 icon 沒繪過，會出現缺字）。Viewbox 負責縮放到
    /// 目標 FontSize，犧牲一點筆畫設計感換取「所有 icon 都看得到」的可靠性。
    /// 屬性架構保留（未來可用），但暫時 hardcode。
    /// </summary>
    private void RefreshResolvedSize()
    {
        SetValue(ResolvedIconSizePropertyKey, FluentCommon.IconSize.Size24);
    }

    /// <summary>
    /// Maps the legacy <c>"Symbol24"</c> string to a
    /// <see cref="FluentCommon.Icon"/> enum value. The trailing size suffix
    /// is stripped because <c>IconSize</c> is set explicitly in the template.
    /// Falls back to <c>Icon.Info</c> when the symbol is unknown so a
    /// missing icon is visible (an info bubble) instead of an empty slot.
    /// </summary>
    private static FluentCommon.Icon ResolveIcon(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return FluentCommon.Icon.Info;

        var name = StripSizeSuffix(symbol);
        return Enum.TryParse<FluentCommon.Icon>(name, ignoreCase: true, out var parsed)
            ? parsed
            : FluentCommon.Icon.Info;
    }

    private static string StripSizeSuffix(string symbol)
    {
        // Existing app callers use names like "Navigation24" or "Star24Filled".
        // The size suffix is fixed at 24 in this app; the FluentIcons enum
        // uses the bare name (e.g., Navigation, Star).
        if (symbol.EndsWith("24Filled", StringComparison.OrdinalIgnoreCase))
            return symbol[..^"24Filled".Length];
        if (symbol.EndsWith("24", StringComparison.OrdinalIgnoreCase))
            return symbol[..^2];
        return symbol;
    }
}
