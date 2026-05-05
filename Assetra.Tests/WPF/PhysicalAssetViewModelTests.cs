using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.WPF.Features.PhysicalAsset;
using Moq;
using Xunit;
using PhysicalAssetEntity = Assetra.Core.Models.MultiAsset.PhysicalAsset;

namespace Assetra.Tests.WPF;

/// <summary>
/// VM-layer tests for PhysicalAssetViewModel. Same scoping rule as
/// InsurancePolicyViewModelTests: avoid paths that route through
/// LoadAsync, which marshals via Application.Current.Dispatcher.
/// </summary>
public sealed class PhysicalAssetViewModelTests
{
    private static PhysicalAssetViewModel CreateVm()
    {
        var repo = new Mock<IPhysicalAssetRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PhysicalAssetEntity>());

        var valuation = new Mock<IPhysicalAssetValuationService>();
        valuation.Setup(v => v.GetSummariesAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Array.Empty<PhysicalAssetSummary>());
        valuation.Setup(v => v.GetTotalCurrentValueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        valuation.Setup(v => v.GetTotalUnrealizedGainAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        return new PhysicalAssetViewModel(repo.Object, valuation.Object);
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
    [InlineData("FormAcquisitionCost", "abc", "購入成本格式錯誤")]
    [InlineData("FormCurrentValue",    "abc", "目前市值格式錯誤")]
    public async Task SaveAsync_InvalidNumber_SetsFormError(string fieldName, string badValue, string expectedError)
    {
        var vm = CreateVm();
        vm.FormName = "Tesla";
        vm.FormAcquisitionCost = "1500000";
        vm.FormCurrentValue = "1200000";
        typeof(PhysicalAssetViewModel).GetProperty(fieldName)!.SetValue(vm, badValue);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expectedError, vm.FormError);
    }

    [Fact]
    public void HasNoAssets_DuringLoad_IsFalse()
    {
        var vm = CreateVm();
        typeof(PhysicalAssetViewModel).GetProperty("IsLoading")!.SetValue(vm, true);

        Assert.False(vm.HasNoAssets, "Empty-state should not flash while a load is in flight.");
    }

    [Fact]
    public void HasNoAssets_AfterLoadWithEmptyResult_IsTrue()
    {
        var vm = CreateVm();

        Assert.True(vm.HasNoAssets);
        Assert.False(vm.HasAssets);
    }

    [Fact]
    public void OpenAddForm_ResetsFormState()
    {
        var vm = CreateVm();
        vm.FormName = "stale";
        vm.FormError = "old error";
        typeof(PhysicalAssetViewModel).GetProperty("EditingId")!.SetValue(vm, Guid.NewGuid());

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
        typeof(PhysicalAssetViewModel).GetProperty("EditingId")!.SetValue(vm, Guid.NewGuid());

        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Equal(string.Empty, vm.FormName);
        Assert.Null(vm.EditingId);
    }

    [Fact]
    public void Constructor_PopulatesCategoryOptions()
    {
        var vm = CreateVm();

        Assert.Equal(Enum.GetValues<PhysicalAssetCategory>().Length, vm.CategoryOptions.Count);
    }
}
