using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.WPF.Features.Retirement;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// VM-layer tests for RetirementViewModel. Avoids LoadAsync paths that
/// dispatch through Application.Current.Dispatcher.
/// </summary>
public sealed class RetirementViewModelTests
{
    private static RetirementViewModel CreateVm()
    {
        var repo = new Mock<IRetirementAccountRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RetirementAccount>());

        var projection = new Mock<IRetirementProjectionService>();
        projection.Setup(p => p.GetAccountSummariesAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Array.Empty<RetirementAccountSummary>());
        projection.Setup(p => p.GetTotalBalanceAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0m);

        return new RetirementViewModel(repo.Object, projection.Object);
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
    [InlineData("FormBalance", "abc", "餘額格式錯誤")]
    [InlineData("FormEmployeeRate", "abc", "員工提撥率格式錯誤")]
    [InlineData("FormEmployerRate", "abc", "雇主提撥率格式錯誤")]
    [InlineData("FormYearsOfService", "abc", "年資格式錯誤")]
    [InlineData("FormLegalWithdrawalAge", "abc", "法定提領年齡格式錯誤")]
    public async Task SaveAsync_InvalidNumber_SetsFormError(string fieldName, string badValue, string expectedError)
    {
        var vm = CreateVm();
        vm.FormName = "勞退新制";
        vm.FormBalance = "100000";
        vm.FormEmployeeRate = "0.06";
        vm.FormEmployerRate = "0.06";
        vm.FormYearsOfService = "10";
        vm.FormLegalWithdrawalAge = "65";
        typeof(RetirementViewModel).GetProperty(fieldName)!.SetValue(vm, badValue);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expectedError, vm.FormError);
    }

    [Fact]
    public void HasNoAccounts_DuringLoad_IsFalse()
    {
        var vm = CreateVm();
        typeof(RetirementViewModel).GetProperty("IsLoading")!.SetValue(vm, true);

        Assert.False(vm.HasNoAccounts);
    }

    [Fact]
    public void HasNoAccounts_AfterLoadWithEmptyResult_IsTrue()
    {
        var vm = CreateVm();

        Assert.True(vm.HasNoAccounts);
        Assert.False(vm.HasAccounts);
    }

    [Fact]
    public void OpenAddForm_ResetsFormState()
    {
        var vm = CreateVm();
        vm.FormName = "stale";
        vm.FormError = "old error";
        typeof(RetirementViewModel).GetProperty("EditingId")!.SetValue(vm, Guid.NewGuid());

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
        typeof(RetirementViewModel).GetProperty("EditingId")!.SetValue(vm, Guid.NewGuid());

        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsFormOpen);
        Assert.Equal(string.Empty, vm.FormName);
        Assert.Null(vm.EditingId);
    }

    [Fact]
    public void Constructor_PopulatesAccountTypeOptions()
    {
        var vm = CreateVm();

        Assert.Equal(Enum.GetValues<RetirementAccountType>().Length, vm.AccountTypeOptions.Count);
    }

    [Theory]
    [InlineData("ProjCurrentAge", "abc", "目前年齡格式錯誤")]
    [InlineData("ProjAnnualReturnRate", "abc", "年化報酬率格式錯誤")]
    [InlineData("ProjAnnualContribution", "abc", "年提撥金額格式錯誤")]
    public async Task ProjectAsync_InvalidInput_SetsProjResult(string fieldName, string badValue, string expectedResult)
    {
        var vm = CreateVm();
        vm.ProjCurrentAge = "30";
        vm.ProjAnnualReturnRate = "0.05";
        vm.ProjAnnualContribution = "60000";
        typeof(RetirementViewModel).GetProperty(fieldName)!.SetValue(vm, badValue);

        var row = new RetirementRowViewModel(
            new RetirementAccountSummary(
                new RetirementAccount(
                    Id: Guid.NewGuid(),
                    Name: "勞退",
                    AccountType: RetirementAccountType.LaborPension,
                    Provider: "",
                    Balance: 100000m,
                    EmployeeContributionRate: 0.06m,
                    EmployerContributionRate: 0.06m,
                    YearsOfService: 10,
                    LegalWithdrawalAge: 65,
                    OpenedDate: DateOnly.FromDateTime(DateTime.Today),
                    Currency: "TWD",
                    Status: RetirementAccountStatus.Active,
                    Notes: null,
                    Version: new()),
                LatestYearContribution: 0m),
            "勞退");

        await vm.ProjectCommand.ExecuteAsync(row);

        Assert.Equal(expectedResult, vm.ProjResult);
    }
}
