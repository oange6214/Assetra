using Assetra.WPF.Features.Portfolio.SubViewModels;
using Xunit;

namespace Assetra.Tests.WPF;

public class PortfolioCompositionViewModelTests
{
    [Fact]
    public void Apply_SplitsEtfVsStock_ByBaseMarketValue()
    {
        var vm = new PortfolioCompositionViewModel();
        vm.Apply(
        [
            (IsEtf: true, MarketValueBase: 4_000_000m),
            (IsEtf: false, MarketValueBase: 6_000_000m),
        ]);

        Assert.True(vm.HasData);
        Assert.Equal(6_000_000m, vm.StockValue);
        Assert.Equal(4_000_000m, vm.EtfValue);
        Assert.Equal(60.0, vm.StockPercent, 1);
        Assert.Equal(40.0, vm.EtfPercent, 1);
    }

    [Fact]
    public void Apply_Empty_HasNoData()
    {
        var vm = new PortfolioCompositionViewModel();
        vm.Apply([]);
        Assert.False(vm.HasData);
        Assert.Equal(0, vm.StockPercent);
    }
}
