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
    public void SameCurrency_DoesNotForceCollapse_AfterUserExpanded()
    {
        var vm = new BuyTxViewModel { IsAdvancedExpanded = true };  // user opened it
        vm.InstrumentCurrency = "TWD";
        vm.CashAccountCurrency = "TWD";          // same currency
        Assert.False(vm.IsCrossCurrency);
        Assert.True(vm.IsAdvancedExpanded);       // 幣別變動不得擅自收合使用者開啟的進階區
    }

    [Fact]
    public void Reset_CollapsesAdvanced()
    {
        var vm = new BuyTxViewModel { IsAdvancedExpanded = true };
        vm.Reset();
        Assert.False(vm.IsAdvancedExpanded);
    }
}
