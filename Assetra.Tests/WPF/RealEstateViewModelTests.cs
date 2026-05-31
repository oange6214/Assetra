using Assetra.Core.Interfaces.MultiAsset;
using Assetra.WPF.Features.RealEstate;
using Moq;
using Xunit;
using RealEstateEntity = Assetra.Core.Models.MultiAsset.RealEstate;

namespace Assetra.Tests.WPF;

/// <summary>
/// VM-layer tests for RealEstateViewModel. Avoids LoadAsync paths that
/// dispatch through Application.Current.Dispatcher (flaky under xUnit).
/// </summary>
public sealed class RealEstateViewModelTests
{
    private static RealEstateViewModel CreateVm()
    {
        var repo = new Mock<IRealEstateRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RealEstateEntity>());

        var valuation = new Mock<IRealEstateValuationService>();
        valuation.Setup(v => v.GetValuationSummariesAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Array.Empty<RealEstateValuationSummary>());
        valuation.Setup(v => v.GetTotalCurrentValueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        valuation.Setup(v => v.GetTotalEquityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        return new RealEstateViewModel(repo.Object, valuation.Object);
    }

    [Fact]
    public async Task SaveAsync_BlankName_SetsFormError()
    {
        var vm = CreateVm();
        vm.IsFormOpen = true;
        vm.FormName = "";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("請輸入名稱", vm.FormError);
    }

    [Theory]
    [InlineData("FormPurchasePrice", "abc", "購入金額格式錯誤")]
    [InlineData("FormCurrentValue", "abc", "目前市值格式錯誤")]
    [InlineData("FormMortgageBalance", "abc", "房貸餘額格式錯誤")]
    public async Task SaveAsync_InvalidNumber_SetsFormError(string fieldName, string badValue, string expectedError)
    {
        var vm = CreateVm();
        vm.FormName = "Apartment";
        vm.FormPurchasePrice = "10000000";
        vm.FormCurrentValue = "12000000";
        vm.FormMortgageBalance = "5000000";
        typeof(RealEstateViewModel).GetProperty(fieldName)!.SetValue(vm, badValue);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expectedError, vm.FormError);
    }

    [Fact]
    public void HasNoProperties_DuringLoad_IsFalse()
    {
        var vm = CreateVm();
        typeof(RealEstateViewModel).GetProperty("IsLoading")!.SetValue(vm, true);

        Assert.False(vm.HasNoProperties);
    }

    [Fact]
    public void HasNoProperties_AfterLoadWithEmptyResult_IsTrue()
    {
        var vm = CreateVm();

        Assert.True(vm.HasNoProperties);
        Assert.False(vm.HasProperties);
    }

    [Fact]
    public void OpenAddForm_ResetsFormState()
    {
        var vm = CreateVm();
        vm.FormName = "stale";
        vm.FormError = "old error";
        typeof(RealEstateViewModel).GetProperty("EditingId")!.SetValue(vm, Guid.NewGuid());

        vm.OpenAddFormCommand.Execute(null);

        Assert.True(vm.IsFormOpen);
        Assert.Equal(string.Empty, vm.FormName);
        Assert.Null(vm.FormError);
        Assert.Null(vm.EditingId);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void CancelEdit_ClearsFormAndClosesIt()
    {
        var vm = CreateVm();
        vm.IsFormOpen = true;
        vm.FormName = "draft";
        typeof(RealEstateViewModel).GetProperty("EditingId")!.SetValue(vm, Guid.NewGuid());

        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Equal(string.Empty, vm.FormName);
        Assert.Null(vm.EditingId);
    }
}
