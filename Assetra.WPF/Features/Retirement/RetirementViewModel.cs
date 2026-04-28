using System.Collections.ObjectModel;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Retirement;

public sealed partial class RetirementViewModel : ObservableObject
{
    private readonly IRetirementAccountRepository _repository;
    private readonly IRetirementProjectionService _projection;

    public ObservableCollection<RetirementRowViewModel> Accounts { get; } = [];
    public IReadOnlyList<RetirementAccountType> AccountTypes { get; } =
        Enum.GetValues<RetirementAccountType>();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _totalBalance;

    // ── Add/Edit form ──
    [ObservableProperty] private string _formName = string.Empty;
    [ObservableProperty] private RetirementAccountType _formAccountType = RetirementAccountType.LaborPension;
    [ObservableProperty] private string _formProvider = string.Empty;
    [ObservableProperty] private string _formBalance = "0";
    [ObservableProperty] private string _formEmployeeRate = "0.06";
    [ObservableProperty] private string _formEmployerRate = "0.06";
    [ObservableProperty] private string _formYearsOfService = "0";
    [ObservableProperty] private string _formLegalWithdrawalAge = "65";
    [ObservableProperty] private DateTime _formOpenedDate = DateTime.Today;
    [ObservableProperty] private string _formCurrency = "TWD";
    [ObservableProperty] private string _formNotes = string.Empty;
    [ObservableProperty] private string? _formError;

    // ── Projection inputs ──
    [ObservableProperty] private string _projCurrentAge = "30";
    [ObservableProperty] private string _projAnnualReturnRate = "0.05";
    [ObservableProperty] private string _projAnnualContribution = "0";
    [ObservableProperty] private string? _projResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private Guid? _editingId;

    public bool IsEditing => EditingId.HasValue;

    public RetirementViewModel(
        IRetirementAccountRepository repository,
        IRetirementProjectionService projection)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(projection);
        _repository = repository;
        _projection = projection;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var summaries = await _projection.GetAccountSummariesAsync().ConfigureAwait(false);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Accounts.Clear();
                foreach (var s in summaries)
                    Accounts.Add(new RetirementRowViewModel(s));
            });
            TotalBalance = await _projection.GetTotalBalanceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName))    { FormError = "請輸入名稱"; return; }
        if (!decimal.TryParse(FormBalance, out var balance))         { FormError = "餘額格式錯誤"; return; }
        if (!decimal.TryParse(FormEmployeeRate, out var empRate))    { FormError = "員工提撥率格式錯誤"; return; }
        if (!decimal.TryParse(FormEmployerRate, out var erRate))     { FormError = "雇主提撥率格式錯誤"; return; }
        if (!int.TryParse(FormYearsOfService, out var years))        { FormError = "年資格式錯誤"; return; }
        if (!int.TryParse(FormLegalWithdrawalAge, out var withAge))  { FormError = "法定提領年齡格式錯誤"; return; }

        var now = DateTimeOffset.UtcNow;
        var deviceId = string.Empty;
        var version = EditingId.HasValue
            ? (await _repository.GetByIdAsync(EditingId.Value).ConfigureAwait(false))?.Version.Bump(deviceId, now)
              ?? EntityVersion.Initial(deviceId, now)
            : EntityVersion.Initial(deviceId, now);

        var entity = new RetirementAccount(
            Id: EditingId ?? Guid.NewGuid(),
            Name: FormName.Trim(),
            AccountType: FormAccountType,
            Provider: FormProvider.Trim(),
            Balance: balance,
            EmployeeContributionRate: empRate,
            EmployerContributionRate: erRate,
            YearsOfService: years,
            LegalWithdrawalAge: withAge,
            OpenedDate: DateOnly.FromDateTime(FormOpenedDate),
            Currency: FormCurrency.Trim(),
            Status: RetirementAccountStatus.Active,
            Notes: string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim(),
            Version: version);

        if (EditingId.HasValue)
            await _repository.UpdateAsync(entity).ConfigureAwait(false);
        else
            await _repository.AddAsync(entity).ConfigureAwait(false);

        ClearForm();
        await LoadAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void Edit(RetirementRowViewModel row)
    {
        EditingId = row.Id;
        FormName = row.Name;
        FormAccountType = row.AccountType;
        FormProvider = row.Provider;
        FormBalance = row.Balance.ToString();
        FormEmployeeRate = row.EmployeeContributionRate.ToString();
        FormEmployerRate = row.EmployerContributionRate.ToString();
        FormYearsOfService = row.YearsOfService.ToString();
        FormLegalWithdrawalAge = row.LegalWithdrawalAge.ToString();
        FormOpenedDate = row.OpenedDate.ToDateTime(TimeOnly.MinValue);
        FormCurrency = row.Currency;
        FormNotes = row.Notes ?? string.Empty;
        FormError = null;
    }

    [RelayCommand]
    private async Task DeleteAsync(RetirementRowViewModel row)
    {
        await _repository.RemoveAsync(row.Id).ConfigureAwait(false);
        await LoadAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ProjectAsync(RetirementRowViewModel row)
    {
        ProjResult = null;
        if (!int.TryParse(ProjCurrentAge, out var age))                    { ProjResult = "目前年齡格式錯誤"; return; }
        if (!decimal.TryParse(ProjAnnualReturnRate, out var rate))         { ProjResult = "年化報酬率格式錯誤"; return; }
        if (!decimal.TryParse(ProjAnnualContribution, out var annualCon))  { ProjResult = "年提撥金額格式錯誤"; return; }

        var p = await _projection.ProjectAsync(row.Id, age, rate, annualCon).ConfigureAwait(false);
        if (p is null) { ProjResult = "找不到帳戶"; return; }

        ProjResult = $"{row.Name}: {p.YearsToWithdrawal} 年後預期 {p.ProjectedBalance:N0}（累計提撥 {p.TotalContributions:N0}）";
    }

    [RelayCommand]
    private void CancelEdit() => ClearForm();

    private void ClearForm()
    {
        EditingId = null;
        FormName = string.Empty;
        FormAccountType = RetirementAccountType.LaborPension;
        FormProvider = string.Empty;
        FormBalance = "0";
        FormEmployeeRate = "0.06";
        FormEmployerRate = "0.06";
        FormYearsOfService = "0";
        FormLegalWithdrawalAge = "65";
        FormOpenedDate = DateTime.Today;
        FormCurrency = "TWD";
        FormNotes = string.Empty;
        FormError = null;
    }
}
