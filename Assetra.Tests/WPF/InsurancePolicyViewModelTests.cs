using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.WPF.Features.Insurance;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// VM-layer tests for InsurancePolicyViewModel. Scope limited to paths that
/// do NOT trigger LoadAsync (which marshals through Application.Current.
/// Dispatcher.InvokeAsync — flaky under xUnit's threading model). The
/// Add-vs-Update routing inside SaveAsync is exercised by the
/// repository sync tests in InsurancePolicySqliteRepositorySyncTests.
/// </summary>
public sealed class InsurancePolicyViewModelTests
{
    private static InsurancePolicyViewModel CreateVm()
    {
        var repo = new Mock<IInsurancePolicyRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InsurancePolicy>());

        var calc = new Mock<IInsuranceCashValueCalculator>();
        calc.Setup(c => c.GetCashValueSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<InsuranceCashValueSummary>());
        calc.Setup(c => c.GetTotalCashValueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0m);
        calc.Setup(c => c.GetTotalAnnualPremiumAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        return new InsurancePolicyViewModel(repo.Object, calc.Object);
    }

    [Fact]
    public async Task SaveAsync_BlankName_SetsFormError()
    {
        var vm = CreateVm();
        vm.IsFormOpen = true;
        vm.FormName = "";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("請輸入保單名稱", vm.FormError);
    }

    [Theory]
    [InlineData("FormAnnualPremium", "abc", "年繳保費格式錯誤")]
    [InlineData("FormFaceValue", "abc", "保額格式錯誤")]
    [InlineData("FormCurrentCashValue", "abc", "現金價值格式錯誤")]
    public async Task SaveAsync_InvalidNumber_SetsFormError(string fieldName, string badValue, string expectedError)
    {
        var vm = CreateVm();
        vm.FormName = "Whole Life";
        // SaveAsync validates premium → faceValue → cashValue in order, so to
        // exercise a specific field's error we have to give the others valid
        // values.
        vm.FormAnnualPremium = "30000";
        vm.FormFaceValue = "1000000";
        vm.FormCurrentCashValue = "150000";
        typeof(InsurancePolicyViewModel).GetProperty(fieldName)!.SetValue(vm, badValue);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expectedError, vm.FormError);
    }

    [Fact]
    public void HasNoPolicies_DuringLoad_IsFalse()
    {
        var vm = CreateVm();
        typeof(InsurancePolicyViewModel).GetProperty("IsLoading")!.SetValue(vm, true);

        Assert.False(vm.HasNoPolicies, "Empty-state should not flash while a load is in flight.");
    }

    [Fact]
    public void HasNoPolicies_AfterLoadWithEmptyResult_IsTrue()
    {
        var vm = CreateVm();

        Assert.True(vm.HasNoPolicies);
        Assert.False(vm.HasPolicies);
    }

    [Fact]
    public void OpenAddForm_ResetsFormState()
    {
        var vm = CreateVm();
        vm.FormName = "stale";
        vm.FormError = "old error";
        typeof(InsurancePolicyViewModel).GetProperty("EditingId")!.SetValue(vm, Guid.NewGuid());

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
        typeof(InsurancePolicyViewModel).GetProperty("EditingId")!.SetValue(vm, Guid.NewGuid());

        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Equal(string.Empty, vm.FormName);
        Assert.Null(vm.EditingId);
    }
}
