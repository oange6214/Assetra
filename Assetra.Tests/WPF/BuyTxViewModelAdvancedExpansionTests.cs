using Assetra.WPF.Features.Portfolio.SubViewModels.Tx;
using Xunit;

namespace Assetra.Tests.WPF;

public class BuyTxViewModelAdvancedExpansionTests
{
    [Fact]
    public void IsAdvancedExpanded_DefaultsFalse()
    {
        var vm = new BuyTxViewModel();
        Assert.False(vm.IsAdvancedExpanded);
    }

    [Fact]
    public void CrossCurrencyTrade_AutoExpandsAdvanced()
    {
        var vm = new BuyTxViewModel();
        vm.InstrumentCurrency = "USD";
        vm.CashAccountCurrency = "TWD";          // USD ≠ TWD → cross-currency
        Assert.True(vm.IsCrossCurrency);
        Assert.True(vm.IsAdvancedExpanded);
    }

    [Fact]
    public void SameCurrency_DoesNotForceCollapse_AfterUserExpanded()
    {
        var vm = new BuyTxViewModel { IsAdvancedExpanded = true };  // user opened it
        vm.InstrumentCurrency = "TWD";
        vm.CashAccountCurrency = "TWD";          // same currency
        Assert.False(vm.IsCrossCurrency);
        Assert.True(vm.IsAdvancedExpanded);       // one-way auto-expand: not force-collapsed
    }

    [Fact]
    public void Reset_CollapsesAdvanced()
    {
        var vm = new BuyTxViewModel { IsAdvancedExpanded = true };
        vm.Reset();
        Assert.False(vm.IsAdvancedExpanded);
    }
}
