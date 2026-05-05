using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Assetra.WPF.Controls;
using Assetra.WPF.Infrastructure.Converters;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class ControlsBehaviorTests
{
    [Fact]
    public void DateRangePicker_TypedStartAfterEndClearsEndDate()
    {
        StaRun(() =>
        {
            EnsureResources();
            var picker = new DateRangePicker
            {
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 1, 31),
            };

            picker.StartDateText = "2026-02-01";

            Assert.Equal(new DateTime(2026, 2, 1), picker.StartDate);
            Assert.Null(picker.EndDate);
        });
    }

    [Fact]
    public void DateRangePicker_TypedEndBeforeStartClearsStartDate()
    {
        StaRun(() =>
        {
            EnsureResources();
            var picker = new DateRangePicker
            {
                StartDate = new DateTime(2026, 2, 1),
                EndDate = new DateTime(2026, 2, 28),
            };

            picker.EndDateText = "2026-01-15";

            Assert.Null(picker.StartDate);
            Assert.Equal(new DateTime(2026, 1, 15), picker.EndDate);
        });
    }

    [Fact]
    public void HexColorPicker_PresetsBindingWorksInsidePopup()
    {
        StaRun(() =>
        {
            EnsureResources();
            var picker = new HexColorPicker
            {
                Presets = new[] { "#112233", "#445566" },
            };

            var itemsControl = GetPrivateField<ItemsControl>(picker, "PresetsItemsControl");

            Assert.NotNull(itemsControl);
            Assert.Same(picker.Presets, itemsControl.ItemsSource);
        });
    }

    private static void StaRun(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
    }

    private static void EnsureResources()
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        var resources = app.Resources;

        AddMissing(resources, "FocusVisual", new Style(typeof(Control)));
        AddMissing(resources, "RadiusSm", new CornerRadius(4));
        AddMissing(resources, "RadiusMd", new CornerRadius(8));
        AddMissing(resources, "FontBase", new FontFamily("Segoe UI"));
        AddMissing(resources, "FontSizeXs", 11d);
        AddMissing(resources, "FontSizeSm", 12d);
        AddMissing(resources, "FontSizeBase", 14d);
        AddMissing(resources, "BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
        AddMissing(resources, "HexToBrushConverter", new HexToBrushConverter());
    }

    private static void AddMissing(ResourceDictionary resources, string key, object? value)
    {
        if (!resources.Contains(key))
            resources.Add(key, value);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

}
