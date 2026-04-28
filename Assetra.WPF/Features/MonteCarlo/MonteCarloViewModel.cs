using System.Collections.ObjectModel;
using Assetra.Core.Interfaces.MonteCarlo;
using Assetra.Core.Models.MonteCarlo;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.MonteCarlo;

public sealed partial class MonteCarloViewModel : ObservableObject
{
    private readonly IMonteCarloSimulator _simulator;

    [ObservableProperty] private string _initialBalance = "10000000";
    [ObservableProperty] private string _annualWithdrawal = "400000";
    [ObservableProperty] private string _meanReturn = "0.05";
    [ObservableProperty] private string _stdDev = "0.12";
    [ObservableProperty] private string _years = "30";
    [ObservableProperty] private string _simulationCount = "1000";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private decimal _successRate;
    [ObservableProperty] private decimal _medianEnding;
    [ObservableProperty] private decimal _p10Ending;
    [ObservableProperty] private decimal _p90Ending;
    [ObservableProperty] private bool _isRunning;

    public ObservableCollection<MonteCarloPathPoint> MedianPath { get; } = [];

    public MonteCarloViewModel(IMonteCarloSimulator simulator)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        _simulator = simulator;
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        ErrorMessage = null;
        if (!decimal.TryParse(InitialBalance, out var init))   { ErrorMessage = "起始餘額格式錯誤"; return; }
        if (!decimal.TryParse(AnnualWithdrawal, out var wd))   { ErrorMessage = "年提領格式錯誤"; return; }
        if (!decimal.TryParse(MeanReturn, out var mu))         { ErrorMessage = "平均報酬率格式錯誤"; return; }
        if (!decimal.TryParse(StdDev, out var sigma) || sigma < 0) { ErrorMessage = "標準差必須 ≥ 0"; return; }
        if (!int.TryParse(Years, out var years) || years <= 0) { ErrorMessage = "年數必須 > 0"; return; }
        if (!int.TryParse(SimulationCount, out var count) || count <= 0) { ErrorMessage = "模擬次數必須 > 0"; return; }

        IsRunning = true;
        try
        {
            var inputs = new MonteCarloInputs(init, wd, mu, sigma, years, count);
            var result = await Task.Run(() => _simulator.Simulate(inputs)).ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SuccessRate = result.SuccessRate;
                MedianEnding = result.MedianEndingBalance;
                P10Ending = result.P10EndingBalance;
                P90Ending = result.P90EndingBalance;

                MedianPath.Clear();
                for (int i = 0; i < result.MedianBalancePath.Count; i++)
                    MedianPath.Add(new MonteCarloPathPoint(i, result.MedianBalancePath[i]));
            });
        }
        finally
        {
            IsRunning = false;
        }
    }
}

public sealed record MonteCarloPathPoint(int Year, decimal Balance);
