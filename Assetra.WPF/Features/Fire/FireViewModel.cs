using System.Collections.ObjectModel;
using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models.Fire;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Fire;

public sealed partial class FireViewModel : ObservableObject
{
    private readonly IFireCalculatorService _calculator;

    [ObservableProperty] private string _currentNetWorth = "1000000";
    [ObservableProperty] private string _annualExpenses = "600000";
    [ObservableProperty] private string _annualSavings = "300000";
    [ObservableProperty] private string _expectedAnnualReturn = "0.05";
    [ObservableProperty] private string _withdrawalRate = "0.04";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _fireNumber;
    [ObservableProperty] private string _yearsToFire = string.Empty;
    [ObservableProperty] private decimal _projectedNetWorthAtFire;

    public ObservableCollection<FireWealthPoint> WealthPath { get; } = [];

    public FireViewModel(IFireCalculatorService calculator)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        _calculator = calculator;
    }

    [RelayCommand]
    private void Calculate()
    {
        ErrorMessage = null;
        if (!decimal.TryParse(CurrentNetWorth, out var nw))   { ErrorMessage = "目前淨資產格式錯誤"; return; }
        if (!decimal.TryParse(AnnualExpenses, out var exp))   { ErrorMessage = "年支出格式錯誤"; return; }
        if (!decimal.TryParse(AnnualSavings, out var sav))    { ErrorMessage = "年儲蓄格式錯誤"; return; }
        if (!decimal.TryParse(ExpectedAnnualReturn, out var r)) { ErrorMessage = "預期報酬率格式錯誤"; return; }
        if (!decimal.TryParse(WithdrawalRate, out var w) || w <= 0m) { ErrorMessage = "提領率必須大於 0"; return; }

        var inputs = new FireInputs(nw, exp, sav, r, w);
        var result = _calculator.Calculate(inputs);

        FireNumber = result.FireNumber;
        YearsToFire = result.YearsToFire?.ToString() ?? "—";
        ProjectedNetWorthAtFire = result.ProjectedNetWorthAtFire;

        WealthPath.Clear();
        for (int i = 0; i < result.WealthPath.Count; i++)
            WealthPath.Add(new FireWealthPoint(i, result.WealthPath[i]));
    }
}

public sealed record FireWealthPoint(int Year, decimal NetWorth);
