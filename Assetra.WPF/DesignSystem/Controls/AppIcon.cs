using System;
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

    static AppIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(typeof(AppIcon)));
        FontSizeProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    }

    public AppIcon()
    {
        Refresh();
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
