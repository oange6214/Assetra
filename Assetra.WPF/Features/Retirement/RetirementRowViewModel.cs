using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Retirement;

public sealed partial class RetirementRowViewModel : ObservableObject
{
    public Guid Id { get; }
    public string Name { get; }
    public RetirementAccountType AccountType { get; }
    [ObservableProperty] private string _accountTypeDisplay = string.Empty;
    public string Provider { get; }
    public decimal Balance { get; }
    public decimal EmployeeContributionRate { get; }
    public decimal EmployerContributionRate { get; }
    public int YearsOfService { get; }
    public int LegalWithdrawalAge { get; }
    public DateOnly OpenedDate { get; }
    public string Currency { get; }
    public RetirementAccountStatus Status { get; }
    public string? Notes { get; }
    public decimal LatestYearContribution { get; }

    public RetirementRowViewModel(RetirementAccountSummary summary, string accountTypeDisplay)
    {
        var a = summary.Account;
        Id = a.Id;
        Name = a.Name;
        AccountType = a.AccountType;
        AccountTypeDisplay = accountTypeDisplay;
        Provider = a.Provider;
        Balance = a.Balance;
        EmployeeContributionRate = a.EmployeeContributionRate;
        EmployerContributionRate = a.EmployerContributionRate;
        YearsOfService = a.YearsOfService;
        LegalWithdrawalAge = a.LegalWithdrawalAge;
        OpenedDate = a.OpenedDate;
        Currency = a.Currency;
        Status = a.Status;
        Notes = a.Notes;
        LatestYearContribution = summary.LatestYearContribution;
    }
}
