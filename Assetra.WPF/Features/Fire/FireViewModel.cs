using System.Collections.ObjectModel;
using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models;
using Assetra.Core.Models.Fire;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Assetra.WPF.Features.Fire;

public sealed partial class FireViewModel : ObservableObject
{
    private readonly IFireCalculatorService _calculator;
    private readonly IFinancialGoalRepository _goals;
    private readonly ISnackbarService? _snackbar;

    [ObservableProperty] private string _currentNetWorth = "1000000";
    [ObservableProperty] private string _annualExpenses = "600000";
    [ObservableProperty] private string _annualSavings = "300000";
    [ObservableProperty] private string _expectedAnnualReturn = "0.05";
    [ObservableProperty] private string _withdrawalRate = "0.04";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _fireNumber;
    [ObservableProperty] private string _yearsToFire = string.Empty;
    [ObservableProperty] private decimal _projectedNetWorthAtFire;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveToGoalsCommand))]
    private bool _hasCalculatedResult;

    public ObservableCollection<FireWealthPoint> WealthPath { get; } = [];

    public FireViewModel(
        IFireCalculatorService calculator,
        IFinancialGoalRepository goals,
        ISnackbarService? snackbar = null)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(goals);
        _calculator = calculator;
        _goals = goals;
        _snackbar = snackbar;
    }

    [RelayCommand]
    private void Calculate()
    {
        ErrorMessage = null;
        HasCalculatedResult = false;
        if (!TryParseDecimal(CurrentNetWorth, out var nw))   { ErrorMessage = "目前淨資產格式錯誤"; return; }
        if (!TryParseDecimal(AnnualExpenses, out var exp))   { ErrorMessage = "年支出格式錯誤"; return; }
        if (!TryParseDecimal(AnnualSavings, out var sav))    { ErrorMessage = "年儲蓄格式錯誤"; return; }
        if (!TryParseDecimal(ExpectedAnnualReturn, out var r)) { ErrorMessage = "預期報酬率格式錯誤"; return; }
        if (!TryParseDecimal(WithdrawalRate, out var w)) { ErrorMessage = "提領率格式錯誤"; return; }

        FireProjection result;
        try
        {
            result = _calculator.Calculate(new FireInputs(nw, exp, sav, r, w));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            ErrorMessage = ToFriendlyError(ex.ParamName);
            return;
        }

        FireNumber = result.FireNumber;
        YearsToFire = result.YearsToFire?.ToString() ?? "—";
        ProjectedNetWorthAtFire = result.ProjectedNetWorthAtFire;
        HasCalculatedResult = true;

        WealthPath.Clear();
        for (int i = 0; i < result.WealthPath.Count; i++)
            WealthPath.Add(new FireWealthPoint(i, result.WealthPath[i]));
    }

    [RelayCommand(CanExecute = nameof(HasCalculatedResult))]
    private async Task SaveToGoalsAsync()
    {
        ErrorMessage = null;
        if (!TryParseDecimal(CurrentNetWorth, out var current))
        {
            ErrorMessage = "目前淨資產格式錯誤";
            return;
        }

        try
        {
            var existing = (await _goals.GetAllAsync().ConfigureAwait(true))
                .FirstOrDefault(g => string.Equals(g.Name, "FIRE", StringComparison.OrdinalIgnoreCase));
            var deadline = int.TryParse(YearsToFire, NumberStyles.Integer, CultureInfo.InvariantCulture, out var years)
                ? DateOnly.FromDateTime(DateTime.Today.AddYears(years))
                : (DateOnly?)null;
            var goal = new FinancialGoal(
                existing?.Id ?? Guid.NewGuid(),
                "FIRE",
                FireNumber,
                current,
                deadline,
                "Generated from FIRE calculator.");

            if (existing is null)
                await _goals.AddAsync(goal).ConfigureAwait(true);
            else
                await _goals.UpdateAsync(goal).ConfigureAwait(true);

            WeakReferenceMessenger.Default.Send(new FireGoalSavedMessage(goal));
            _snackbar?.Success("已同步到財務目標");
        }
        catch (Exception ex)
        {
            _snackbar?.Error(ex.Message);
        }
    }

    private static bool TryParseDecimal(string? input, out decimal value) =>
        decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
        || decimal.TryParse(input, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    private static string ToFriendlyError(string? paramName) => paramName switch
    {
        nameof(FireInputs.CurrentNetWorth) => "目前淨資產不可為負數",
        nameof(FireInputs.AnnualExpenses) => "年支出必須大於 0",
        nameof(FireInputs.AnnualSavings) => "年儲蓄不可為負數",
        nameof(FireInputs.ExpectedAnnualReturn) => "預期報酬率必須大於 -100%",
        nameof(FireInputs.WithdrawalRate) => "安全提領率必須大於 0 且不超過 100%",
        nameof(FireInputs.MaxYears) => "模擬年數必須大於 0",
        _ => "FIRE 輸入值無效",
    };
}

public sealed record FireWealthPoint(int Year, decimal NetWorth);

public sealed class FireGoalSavedMessage(FinancialGoal goal) : ValueChangedMessage<FinancialGoal>(goal);
