using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Assetra.WPF.DesignSystem.Controls;

public sealed class AppIcon : Control
{
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(
            nameof(Symbol),
            typeof(string),
            typeof(AppIcon),
            new FrameworkPropertyMetadata("Info24", FrameworkPropertyMetadataOptions.AffectsRender, OnIconPropertyChanged));

    public static readonly DependencyProperty FilledProperty =
        DependencyProperty.Register(
            nameof(Filled),
            typeof(bool),
            typeof(AppIcon),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIconPropertyChanged));

    private static readonly IReadOnlyDictionary<string, Geometry> Icons = CreateIcons();

    private static readonly DependencyPropertyKey DataPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Data),
            typeof(Geometry),
            typeof(AppIcon),
            new FrameworkPropertyMetadata(Icons["Info24"], FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DataProperty = DataPropertyKey.DependencyProperty;

    static AppIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(typeof(AppIcon)));
        FontSizeProperty.OverrideMetadata(typeof(AppIcon), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    }

    public AppIcon()
    {
        SetValue(DataPropertyKey, Resolve(Symbol, Filled));
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

    public Geometry Data => (Geometry)GetValue(DataProperty);

    private static void OnIconPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is AppIcon icon)
        {
            icon.SetValue(DataPropertyKey, Resolve(icon.Symbol, icon.Filled));
        }
    }

    private static Geometry Resolve(string? symbol, bool filled)
    {
        if (string.Equals(symbol, "Star24", StringComparison.OrdinalIgnoreCase) && filled && Icons.TryGetValue("StarFilled24", out var filledStar))
        {
            return filledStar;
        }

        if (symbol is not null && Icons.TryGetValue(symbol, out var geometry))
        {
            return geometry;
        }

        return Icons["Info24"];
    }

    private static IReadOnlyDictionary<string, Geometry> CreateIcons()
    {
        static Geometry Icon(string path)
        {
            var geometry = Geometry.Parse(path);
            geometry.Freeze();
            return geometry;
        }

        return new Dictionary<string, Geometry>(StringComparer.OrdinalIgnoreCase)
        {
            ["Add24"] = Icon("M11 5 H13 V11 H19 V13 H13 V19 H11 V13 H5 V11 H11 Z"),
            ["Alert24"] = Icon("M12 3 C9.2 3 7 5.2 7 8 V11.5 C7 13 6.4 14.3 5.3 15.4 L4 16.7 V18 H20 V16.7 L18.7 15.4 C17.6 14.3 17 13 17 11.5 V8 C17 5.2 14.8 3 12 3 Z M9 20 H15 C14.5 21.2 13.4 22 12 22 C10.6 22 9.5 21.2 9 20 Z"),
            ["ArrowRepeatAll24"] = Icon("M17 2 L22 7 L17 12 V9 H8 C6.3 9 5 10.3 5 12 H3 C3 9.2 5.2 7 8 7 H17 Z M7 22 L2 17 L7 12 V15 H16 C17.7 15 19 13.7 19 12 H21 C21 14.8 18.8 17 16 17 H7 Z"),
            ["ArrowSync24"] = Icon("M12 4 C16.4 4 20 7.6 20 12 H22 L18.5 15.5 L15 12 H18 C18 8.7 15.3 6 12 6 V4 Z M12 20 C7.6 20 4 16.4 4 12 H2 L5.5 8.5 L9 12 H6 C6 15.3 8.7 18 12 18 V20 Z"),
            ["Box24"] = Icon("M12 2 L21 7 V17 L12 22 L3 17 V7 Z M12 4.3 L6.2 7.5 L12 10.8 L17.8 7.5 Z M5 9.2 V15.8 L11 19.1 V12.5 Z M13 19.1 L19 15.8 V9.2 L13 12.5 Z"),
            ["Briefcase24"] = Icon("M9 4 H15 C16.1 4 17 4.9 17 6 V7 H21 V19 H3 V7 H7 V6 C7 4.9 7.9 4 9 4 Z M9 7 H15 V6 H9 Z M5 9 V17 H19 V9 H5 Z M10 12 H14 V14 H10 Z"),
            ["Calculator24"] = Icon("M6 3 H18 C19.1 3 20 3.9 20 5 V19 C20 20.1 19.1 21 18 21 H6 C4.9 21 4 20.1 4 19 V5 C4 3.9 4.9 3 6 3 Z M6 5 V9 H18 V5 Z M7 12 H9 V14 H7 Z M11 12 H13 V14 H11 Z M15 12 H17 V14 H15 Z M7 16 H9 V18 H7 Z M11 16 H13 V18 H11 Z M15 16 H17 V18 H15 Z"),
            ["CalendarLtr24"] = Icon("M7 2 H9 V5 H15 V2 H17 V5 H20 V21 H4 V5 H7 Z M6 9 V19 H18 V9 Z M8 11 H10 V13 H8 Z M12 11 H14 V13 H12 Z M8 15 H10 V17 H8 Z M12 15 H14 V17 H12 Z"),
            ["Call24"] = Icon("M6.5 3 H10 L11.5 8 L9 10 C10.1 12.2 11.8 13.9 14 15 L16 12.5 L21 14 V17.5 C21 19.4 19.4 21 17.5 21 C9.5 21 3 14.5 3 6.5 C3 4.6 4.6 3 6.5 3 Z"),
            ["ChartMultiple24"] = Icon("M4 19 H21 V21 H2 V3 H4 Z M7 17 V11 H10 V17 Z M12 17 V6 H15 V17 Z M17 17 V9 H20 V17 Z"),
            ["Checkmark24"] = Icon("M9.2 16.2 L4.8 11.8 L3.4 13.2 L9.2 19 L21 7.2 L19.6 5.8 Z"),
            ["CheckmarkCircle24"] = Icon("M12 3 C7 3 3 7 3 12 C3 17 7 21 12 21 C17 21 21 17 21 12 C21 7 17 3 12 3 Z M10.5 15.5 L7.5 12.5 L8.9 11.1 L10.5 12.7 L15.6 7.6 L17 9 Z"),
            ["ChevronDown24"] = Icon("M6.4 8.6 L12 14.2 L17.6 8.6 L19 10 L12 17 L5 10 Z"),
            ["ChevronLeft24"] = Icon("M15.4 5 L16.8 6.4 L11.2 12 L16.8 17.6 L15.4 19 L8.4 12 Z"),
            ["ChevronRight24"] = Icon("M8.6 5 L15.6 12 L8.6 19 L7.2 17.6 L12.8 12 L7.2 6.4 Z"),
            ["ChevronUp24"] = Icon("M6.4 15.4 L12 9.8 L17.6 15.4 L19 14 L12 7 L5 14 Z"),
            ["Cut24"] = Icon("M4.5 4 L11.5 11 L13 9.5 L17.5 5 H20 L12.8 12.2 L20 19 H17.5 L11.5 13 L9.8 14.7 C10 15.1 10.1 15.5 10.1 16 C10.1 18 8.5 19.6 6.5 19.6 C4.5 19.6 2.9 18 2.9 16 C2.9 14 4.5 12.4 6.5 12.4 C7 12.4 7.5 12.5 7.9 12.7 L9.5 11.1 L2 4 Z M6.5 14.4 C5.6 14.4 4.9 15.1 4.9 16 C4.9 16.9 5.6 17.6 6.5 17.6 C7.4 17.6 8.1 16.9 8.1 16 C8.1 15.1 7.4 14.4 6.5 14.4 Z"),
            ["DataPie24"] = Icon("M13 3 C17.6 3.5 21 7.4 21 12 H13 Z M11 4 C6.5 4.5 3 8.3 3 13 C3 17.4 6.6 21 11 21 C14.9 21 18.1 18.3 18.8 14.7 H11 Z"),
            ["DataTrending24"] = Icon("M4 19 H21 V21 H2 V3 H4 Z M6 15 L10 11 L13 14 L19 8 V12 H21 V5 H14 V7 H17.6 L13 11.6 L10 8.6 L4.8 13.8 Z"),
            ["Delete24"] = Icon("M9 3 H15 L16 5 H21 V7 H3 V5 H8 Z M6 9 H18 L17 21 H7 Z M9 11 V19 H11 V11 Z M13 11 V19 H15 V11 Z"),
            ["Dismiss24"] = Icon("M6.4 5 L5 6.4 L10.6 12 L5 17.6 L6.4 19 L12 13.4 L17.6 19 L19 17.6 L13.4 12 L19 6.4 L17.6 5 L12 10.6 Z"),
            ["DocumentArrowDown24"] = Icon("M6 3 H14 L20 9 V21 H6 Z M14 5.5 V10 H18.5 Z M11 11 H13 V15 L15 13 L16.4 14.4 L12 18.8 L7.6 14.4 L9 13 L11 15 Z"),
            ["DocumentBulletList24"] = Icon("M6 3 H15 L20 8 V21 H6 Z M14 5.5 V9 H17.5 Z M9 11 H10.5 V12.5 H9 Z M12 11 H17 V12.5 H12 Z M9 15 H10.5 V16.5 H9 Z M12 15 H17 V16.5 H12 Z"),
            ["DualScreenSpan24"] = Icon("M4 5 H15 V16 H4 Z M6 7 V14 H13 V7 Z M17 8 H20 V19 H9 V18 H18 V10 H17 Z"),
            ["Edit24"] = Icon("M17.8 3.2 C18.6 2.4 19.9 2.4 20.7 3.2 C21.5 4 21.5 5.3 20.7 6.1 L9.2 17.6 L4 19 L5.4 13.8 Z M16.4 4.6 L6.9 14.1 L6.3 16.7 L8.9 16.1 L18.4 6.6 Z"),
            ["Flash24"] = Icon("M13 2 L4 14 H11 L9 22 L20 9 H13 Z"),
            ["Home24"] = Icon("M12 3 L21 11 V21 H15 V15 H9 V21 H3 V11 Z M5 12 V19 H7 V13 H17 V19 H19 V12 L12 5.8 Z"),
            ["Info24"] = Icon("M12 3 C7 3 3 7 3 12 C3 17 7 21 12 21 C17 21 21 17 21 12 C21 7 17 3 12 3 Z M11 10 H13 V17 H11 Z M11 7 H13 V9 H11 Z"),
            ["Money24"] = Icon("M3 6 H21 V18 H3 Z M5 8 V16 H19 V8 Z M12 9.5 C13.4 9.5 14.5 10.6 14.5 12 C14.5 13.4 13.4 14.5 12 14.5 C10.6 14.5 9.5 13.4 9.5 12 C9.5 10.6 10.6 9.5 12 9.5 Z M6 10 H8 V14 H6 Z M16 10 H18 V14 H16 Z"),
            ["Navigation24"] = Icon("M4 6 H20 V8 H4 Z M4 11 H20 V13 H4 Z M4 16 H20 V18 H4 Z"),
            ["People24"] = Icon("M9 4 C11.2 4 13 5.8 13 8 C13 10.2 11.2 12 9 12 C6.8 12 5 10.2 5 8 C5 5.8 6.8 4 9 4 Z M15.5 6 C17.4 6 19 7.6 19 9.5 C19 11.4 17.4 13 15.5 13 C14.7 13 14 12.8 13.4 12.3 C14.4 11.2 15 9.7 15 8 C15 7.3 14.9 6.6 14.6 6.1 C14.9 6 15.2 6 15.5 6 Z M2.5 20 C3.3 16.5 5.7 14.5 9 14.5 C12.3 14.5 14.7 16.5 15.5 20 Z M15 20 C14.7 18.2 14 16.8 13 15.8 C13.8 15.3 14.6 15 15.5 15 C18.5 15 20.6 16.8 21.5 20 Z"),
            ["PersonClock24"] = Icon("M9 4 C11.2 4 13 5.8 13 8 C13 10.2 11.2 12 9 12 C6.8 12 5 10.2 5 8 C5 5.8 6.8 4 9 4 Z M2.5 20 C3.3 16.5 5.7 14.5 9 14.5 C10.1 14.5 11.1 14.7 12 15.1 C11.4 16 11 17 11 18 C11 18.7 11.1 19.4 11.4 20 Z M17 13 C19.8 13 22 15.2 22 18 C22 20.8 19.8 23 17 23 C14.2 23 12 20.8 12 18 C12 15.2 14.2 13 17 13 Z M16.2 15.5 V18.5 L18.7 20 L19.5 18.7 L17.8 17.7 V15.5 Z"),
            ["Search24"] = Icon("M10 4 C6.7 4 4 6.7 4 10 C4 13.3 6.7 16 10 16 C11.3 16 12.5 15.6 13.5 14.9 L18.6 20 L20 18.6 L14.9 13.5 C15.6 12.5 16 11.3 16 10 C16 6.7 13.3 4 10 4 Z M10 6 C12.2 6 14 7.8 14 10 C14 12.2 12.2 14 10 14 C7.8 14 6 12.2 6 10 C6 7.8 7.8 6 10 6 Z"),
            ["Settings24"] = Icon("M10.8 2 H13.2 L13.8 5 C14.5 5.2 15.1 5.5 15.7 5.9 L18.5 4.7 L19.8 6.8 L17.5 8.7 C17.6 9.1 17.7 9.5 17.7 10 C17.7 10.5 17.6 10.9 17.5 11.3 L19.8 13.2 L18.5 15.3 L15.7 14.1 C15.1 14.5 14.5 14.8 13.8 15 L13.2 18 H10.8 L10.2 15 C9.5 14.8 8.9 14.5 8.3 14.1 L5.5 15.3 L4.2 13.2 L6.5 11.3 C6.4 10.9 6.3 10.5 6.3 10 C6.3 9.5 6.4 9.1 6.5 8.7 L4.2 6.8 L5.5 4.7 L8.3 5.9 C8.9 5.5 9.5 5.2 10.2 5 Z M12 8 C10.9 8 10 8.9 10 10 C10 11.1 10.9 12 12 12 C13.1 12 14 11.1 14 10 C14 8.9 13.1 8 12 8 Z"),
            ["Shield24"] = Icon("M12 2 L20 5 V11 C20 16 16.7 20 12 22 C7.3 20 4 16 4 11 V5 Z M12 4.2 L6 6.4 V11 C6 14.7 8.3 17.8 12 19.8 C15.7 17.8 18 14.7 18 11 V6.4 Z"),
            ["Square24"] = Icon("M6 5 H18 C18.6 5 19 5.4 19 6 V18 C19 18.6 18.6 19 18 19 H6 C5.4 19 5 18.6 5 18 V6 C5 5.4 5.4 5 6 5 Z M7 7 V17 H17 V7 Z"),
            ["Star24"] = Icon("M12 4 L14.2 8.5 L19.2 9.2 L15.6 12.7 L16.4 17.7 L12 15.4 L7.6 17.7 L8.4 12.7 L4.8 9.2 L9.8 8.5 Z"),
            ["StarFilled24"] = Icon("M12 2 L15.1 8.6 L22 9.3 L16.9 14 L18.2 21 L12 17.4 L5.8 21 L7.1 14 L2 9.3 L8.9 8.6 Z"),
            ["Subtract24"] = Icon("M5 11 H19 V13 H5 Z"),
            ["Tag24"] = Icon("M3 4 H12 L21 13 L13 21 L4 12 Z M7.5 7 C6.7 7 6 7.7 6 8.5 C6 9.3 6.7 10 7.5 10 C8.3 10 9 9.3 9 8.5 C9 7.7 8.3 7 7.5 7 Z"),
            ["Target24"] = Icon("M12 3 C17 3 21 7 21 12 C21 17 17 21 12 21 C7 21 3 17 3 12 C3 7 7 3 12 3 Z M12 5 C8.1 5 5 8.1 5 12 C5 15.9 8.1 19 12 19 C15.9 19 19 15.9 19 12 C19 8.1 15.9 5 12 5 Z M12 8 C14.2 8 16 9.8 16 12 C16 14.2 14.2 16 12 16 C9.8 16 8 14.2 8 12 C8 9.8 9.8 8 12 8 Z M12 10.5 C11.2 10.5 10.5 11.2 10.5 12 C10.5 12.8 11.2 13.5 12 13.5 C12.8 13.5 13.5 12.8 13.5 12 C13.5 11.2 12.8 10.5 12 10.5 Z"),
            ["Warning24"] = Icon("M12 3 L22 20 H2 Z M12 7 L5.4 18 H18.6 Z M11 10 H13 V14 H11 Z M11 16 H13 V18 H11 Z"),
            ["WeatherMoon24"] = Icon("M15.8 3.4 C14.9 4.9 14.5 6.4 14.5 8 C14.5 12.1 17.9 15.5 22 15.5 C20.6 18.8 17.4 21 13.8 21 C8.8 21 4.8 17 4.8 12 C4.8 8 7.4 4.6 11.1 3.5 C10.8 4.5 10.6 5.5 10.6 6.6 C10.6 11.4 14.6 15.4 19.4 15.4 C18 16.9 16 17.8 13.8 17.8 C10.6 17.8 7.9 15.2 7.9 12 C7.9 9.5 9.4 7.3 11.6 6.5 C11.7 4.9 12.4 3.4 13.5 2.3 C14.3 2.5 15.1 2.8 15.8 3.4 Z"),
            ["WeatherSunny24"] = Icon("M11 2 H13 V5 H11 Z M11 19 H13 V22 H11 Z M4.2 3.8 L6.3 5.9 L4.9 7.3 L2.8 5.2 Z M17.7 18.1 L19.8 20.2 L18.4 21.6 L16.3 19.5 Z M2 11 H5 V13 H2 Z M19 11 H22 V13 H19 Z M4.9 16.7 L6.3 18.1 L4.2 20.2 L2.8 18.8 Z M19.8 3.8 L21.2 5.2 L19.1 7.3 L17.7 5.9 Z M12 7 C14.8 7 17 9.2 17 12 C17 14.8 14.8 17 12 17 C9.2 17 7 14.8 7 12 C7 9.2 9.2 7 12 7 Z M12 9 C10.3 9 9 10.3 9 12 C9 13.7 10.3 15 12 15 C13.7 15 15 13.7 15 12 C15 10.3 13.7 9 12 9 Z"),
        };
    }
}
