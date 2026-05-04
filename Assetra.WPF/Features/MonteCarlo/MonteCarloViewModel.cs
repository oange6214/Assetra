using System.Collections.ObjectModel;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.MonteCarlo;
using Assetra.Core.Models.MonteCarlo;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.MonteCarlo;

public sealed partial class MonteCarloViewModel : ObservableObject
{
    private readonly IMonteCarloSimulator _simulator;

    [ObservableProperty] private string _initialBalance = "10,000,000";
    [ObservableProperty] private string _annualWithdrawal = "400,000";
    [ObservableProperty] private string _meanReturn = "0.05";
    [ObservableProperty] private string _stdDev = "0.12";
    [ObservableProperty] private string _years = "30";
    [ObservableProperty] private string _simulationCount = "1,000";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _successRate;
    [ObservableProperty] private decimal _medianEnding;
    [ObservableProperty] private decimal _p10Ending;
    [ObservableProperty] private decimal _p90Ending;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasResult;

    public ObservableCollection<MonteCarloPathPoint> MedianPath { get; } = [];

    private readonly ILocalizationService? _localization;

    public MonteCarloViewModel(
        IMonteCarloSimulator simulator,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        _simulator = simulator;
        _localization = localization;
    }

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;

    private bool CanRun() => !IsRunning;

    partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (IsRunning)
            return;

        ErrorMessage = null;
        if (!ParseHelpers.TryParseDecimal(InitialBalance, out var init))      { ErrorMessage = L("MonteCarlo.Error.InitialInvalid",     "起始餘額格式錯誤"); return; }
        if (!ParseHelpers.TryParseDecimal(AnnualWithdrawal, out var wd))      { ErrorMessage = L("MonteCarlo.Error.WithdrawalInvalid",  "年提領格式錯誤");   return; }
        if (!ParseHelpers.TryParseDecimal(MeanReturn, out var mu) || mu <= -1m) { ErrorMessage = L("MonteCarlo.Error.MeanInvalid",     "平均報酬率必須 > -100%"); return; }
        if (!ParseHelpers.TryParseDecimal(StdDev, out var sigma) || sigma < 0) { ErrorMessage = L("MonteCarlo.Error.StdDevInvalid",    "標準差必須 ≥ 0");   return; }
        if (!ParseHelpers.TryParseInt(Years, out var years) || years <= 0)    { ErrorMessage = L("MonteCarlo.Error.YearsInvalid",      "年數必須 > 0");    return; }
        if (years > MonteCarloInputs.MaxYears) { ErrorMessage = string.Format(L("MonteCarlo.Error.YearsMax", "年數必須 ≤ {0}"), MonteCarloInputs.MaxYears); return; }
        if (!ParseHelpers.TryParseInt(SimulationCount, out var count) || count <= 0) { ErrorMessage = L("MonteCarlo.Error.CountInvalid", "模擬次數必須 > 0"); return; }
        if (count > MonteCarloInputs.MaxSimulationCount) { ErrorMessage = string.Format(L("MonteCarlo.Error.CountMax", "模擬次數必須 ≤ {0:N0}"), MonteCarloInputs.MaxSimulationCount); return; }

        IsRunning = true;
        try
        {
            var inputs = new MonteCarloInputs(init, wd, mu, sigma, years, count);
            var result = await Task.Run(() => _simulator.Simulate(inputs));

            SuccessRate = result.SuccessRate;
            MedianEnding = result.MedianEndingBalance;
            P10Ending = result.P10EndingBalance;
            P90Ending = result.P90EndingBalance;

            MedianPath.Clear();
            for (int i = 0; i < result.MedianBalancePath.Count; i++)
                MedianPath.Add(new MonteCarloPathPoint(i, result.MedianBalancePath[i]));

            HasResult = true;
        }
        finally
        {
            IsRunning = false;
        }
    }
}

public sealed record MonteCarloPathPoint(int Year, decimal Balance);
