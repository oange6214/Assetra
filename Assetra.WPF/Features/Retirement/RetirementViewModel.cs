using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Retirement;

public sealed record RetirementAccountTypeOption(RetirementAccountType Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed partial class RetirementViewModel : ObservableObject
{
    private readonly IRetirementAccountRepository _repository;
    private readonly IRetirementProjectionService _projection;
    private readonly ILocalizationService _localization;
    private readonly Func<string> _deviceIdProvider;
    private readonly TimeProvider _time;

    public ObservableCollection<RetirementRowViewModel> Accounts { get; } = [];
    public ObservableCollection<RetirementAccountTypeOption> AccountTypeOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoAccounts))]
    private bool _isLoading;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _totalBalance;

    public bool HasAccounts => Accounts.Count > 0;
    public bool HasNoAccounts => !IsLoading && Accounts.Count == 0;

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
    [ObservableProperty] private bool _isFormOpen;

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
        IRetirementProjectionService projection,
        Func<string>? deviceIdProvider = null,
        TimeProvider? time = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(projection);
        _repository = repository;
        _projection = projection;
        _localization = localization ?? NullLocalizationService.Instance;
        _deviceIdProvider = deviceIdProvider ?? (() => "local");
        _time = time ?? TimeProvider.System;
        Accounts.CollectionChanged += (_, _) => NotifyListStateChanged();
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshLocalizedAccountTypeText();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var summaries = await _projection.GetAccountSummariesAsync().ConfigureAwait(true);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Accounts.Clear();
                foreach (var s in summaries)
                    Accounts.Add(new RetirementRowViewModel(
                        s,
                        GetAccountTypeDisplayName(s.Account.AccountType)));
            });
            TotalBalance = await _projection.GetTotalBalanceAsync().ConfigureAwait(true);
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
    private void OpenAddForm()
    {
        ClearForm();
        IsFormOpen = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName))    { FormError = "請輸入名稱"; return; }
        if (!ParseHelpers.TryParseDecimal(FormBalance, out var balance))         { FormError = "餘額格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(FormEmployeeRate, out var empRate))    { FormError = "員工提撥率格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(FormEmployerRate, out var erRate))     { FormError = "雇主提撥率格式錯誤"; return; }
        if (!int.TryParse(FormYearsOfService, out var years))        { FormError = "年資格式錯誤"; return; }
        if (!int.TryParse(FormLegalWithdrawalAge, out var withAge))  { FormError = "法定提領年齡格式錯誤"; return; }

        var now = _time.GetUtcNow();
        var deviceId = CurrentDeviceId();
        var version = EditingId.HasValue
            ? (await _repository.GetByIdAsync(EditingId.Value).ConfigureAwait(true))?.Version.Bump(deviceId, now)
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
            await _repository.UpdateAsync(entity).ConfigureAwait(true);
        else
            await _repository.AddAsync(entity).ConfigureAwait(true);

        ClearForm();
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Edit(RetirementRowViewModel row)
    {
        EditingId = row.Id;
        IsFormOpen = true;
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
        await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ProjectAsync(RetirementRowViewModel row)
    {
        ProjResult = null;
        if (!int.TryParse(ProjCurrentAge, out var age))                    { ProjResult = "目前年齡格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(ProjAnnualReturnRate, out var rate))         { ProjResult = "年化報酬率格式錯誤"; return; }
        if (!ParseHelpers.TryParseDecimal(ProjAnnualContribution, out var annualCon))  { ProjResult = "年提撥金額格式錯誤"; return; }

        var p = await _projection.ProjectAsync(row.Id, age, rate, annualCon).ConfigureAwait(true);
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
        IsFormOpen = false;
    }

    private string CurrentDeviceId()
    {
        var device = _deviceIdProvider();
        return string.IsNullOrWhiteSpace(device) ? "local" : device;
    }

    private void NotifyListStateChanged()
    {
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasNoAccounts));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLocalizedAccountTypeText();

    private void RefreshLocalizedAccountTypeText()
    {
        AccountTypeOptions.Clear();
        foreach (var accountType in Enum.GetValues<RetirementAccountType>())
            AccountTypeOptions.Add(new RetirementAccountTypeOption(accountType, GetAccountTypeDisplayName(accountType)));

        foreach (var account in Accounts)
            account.AccountTypeDisplay = GetAccountTypeDisplayName(account.AccountType);
    }

    private string GetAccountTypeDisplayName(RetirementAccountType accountType)
        => _localization.Get($"Retirement.AccountType.{accountType}", GetAccountTypeFallback(accountType));

    private static string GetAccountTypeFallback(RetirementAccountType accountType)
        => accountType switch
        {
            RetirementAccountType.LaborPension => "勞退",
            RetirementAccountType.EmployerSponsored => "雇主退休計畫",
            RetirementAccountType.IndividualRetirementAccount => "個人退休帳戶",
            RetirementAccountType.Annuity => "年金",
            RetirementAccountType.Other => "其他",
            _ => accountType.ToString(),
        };
}
