using Assetra.Core.Dtos;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// Guards <see cref="AllocationPanelViewModel.Apply"/> reconciliation. The Apply
/// body was extracted into ApplyCore behind a Dispatcher marshal (fixing a
/// background-thread race that exposed a null slice during the PieSeries
/// projection); these tests pin the reconciliation + dirty-check behavior that
/// the marshal must not change. In headless tests Application.Current is null,
/// so Apply runs ApplyCore inline.
/// </summary>
public class AllocationPanelViewModelTests
{
    [Fact]
    public void Apply_EmptySlices_HasNoDataAndEmptyPieSeries()
    {
        var vm = new AllocationPanelViewModel(localization: null);

        vm.Apply([]);

        Assert.False(vm.HasData);
        Assert.Empty(vm.Slices);
        Assert.Empty(vm.PieSeries);
    }

    [Fact]
    public void Apply_BuildsOneSliceAndSeriesPerResult_WithMappedColors()
    {
        var vm = new AllocationPanelViewModel(localization: null);

        vm.Apply(
        [
            new AllocationSliceResult(AllocationSliceKind.AssetType, 1000m, 50m, AssetType.Stock),
            new AllocationSliceResult(AllocationSliceKind.Cash, 600m, 30m),
            new AllocationSliceResult(AllocationSliceKind.Liabilities, 400m, 20m),
        ]);

        Assert.True(vm.HasData);
        Assert.Equal(3, vm.Slices.Count);
        Assert.Equal(3, vm.PieSeries.Length);
        // Color comes from the AssetType→color map / fixed cash & liability colors.
        Assert.Equal("#3B82F6", vm.Slices[0].ColorHex); // Stock
        Assert.Equal("#94A3B8", vm.Slices[1].ColorHex); // Cash
        Assert.Equal("#EF4444", vm.Slices[2].ColorHex); // Liabilities
        Assert.Equal(1000m, vm.Slices[0].Value);
    }

    [Fact]
    public void Apply_UnknownAssetType_IsSkipped()
    {
        var vm = new AllocationPanelViewModel(localization: null);

        // Etf has no entry in the color map → must be skipped, not crash.
        vm.Apply(
        [
            new AllocationSliceResult(AllocationSliceKind.AssetType, 100m, 100m, AssetType.Etf),
        ]);

        Assert.False(vm.HasData);
        Assert.Empty(vm.Slices);
    }

    [Fact]
    public void Apply_CalledTwiceWithEquivalentData_DoesNotRebuildPieSeries()
    {
        var vm = new AllocationPanelViewModel(localization: null);

        vm.Apply([new AllocationSliceResult(AllocationSliceKind.Cash, 600m, 30m)]);
        var firstSeries = vm.PieSeries;

        // Same label + same value rounded to the nearest integer → the dirty-check
        // must short-circuit so LiveCharts isn't reset (no animation flicker / GC).
        vm.Apply([new AllocationSliceResult(AllocationSliceKind.Cash, 600.4m, 30m)]);

        Assert.Same(firstSeries, vm.PieSeries);
    }

    [Fact]
    public void Apply_CalledWithChangedData_RebuildsPieSeries()
    {
        var vm = new AllocationPanelViewModel(localization: null);

        vm.Apply([new AllocationSliceResult(AllocationSliceKind.Cash, 600m, 30m)]);
        var firstSeries = vm.PieSeries;

        vm.Apply([new AllocationSliceResult(AllocationSliceKind.Cash, 900m, 45m)]);

        Assert.NotSame(firstSeries, vm.PieSeries);
    }
}
