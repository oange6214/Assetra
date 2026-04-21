using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Alerts;

public partial class AlertsViewModel : ObservableObject, IDisposable
{
    private readonly IAlertRepository _repo;
    private readonly IStockSearchService _search;
    private readonly ISnackbarService _snackbar;
    private readonly ILocalizationService _localization;
    private readonly ICurrencyService? _currencyService;
    private IDisposable? _subscription;

    public ObservableCollection<AlertRowViewModel> Rules { get; } = [];

    // Add form
    [ObservableProperty] private string _addSymbol = string.Empty;
    [ObservableProperty] private string _addTargetPrice = string.Empty;
    [ObservableProperty] private string _addCondition = "突破";
    [ObservableProperty] private string _addError = string.Empty;
    [ObservableProperty] private bool _hasNoRules = true;

    // NavRail badge
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTriggeredAlerts))]
    [NotifyPropertyChangedFor(nameof(TriggeredBadge))]
    private int _triggeredCount;

    public bool HasTriggeredAlerts => TriggeredCount > 0;
    public string TriggeredBadge => TriggeredCount > 99 ? "99+" : TriggeredCount.ToString();

    public IReadOnlyList<string> Conditions { get; } = ["突破", "跌破"];

    public AlertsViewModel(
        IAlertRepository repo,
        IStockSearchService search,
        IStockService stockService,
        IScheduler uiScheduler,
        ISnackbarService snackbar,
        ILocalizationService localization,
        ICurrencyService? currencyService = null)
    {
        _repo = repo;
        _search = search;
        _snackbar = snackbar;
        _localization = localization;
        _currencyService = currencyService;

        _subscription = stockService.QuoteStream
            .ObserveOn(uiScheduler)
            .Subscribe(CheckAlerts);

        if (currencyService is not null)
            currencyService.CurrencyChanged += OnCurrencyChanged;
    }

    public async Task LoadAsync()
    {
        var rules = await _repo.GetRulesAsync();
        Rules.Clear();
        foreach (var r in rules)
            Rules.Add(ToRow(r));
        HasNoRules = Rules.Count == 0;
        TriggeredCount = Rules.Count(r => r.IsTriggered);
    }

    [RelayCommand]
    private async Task AddRule()
    {
        AddError = string.Empty;
        if (string.IsNullOrWhiteSpace(AddSymbol))
        { AddError = "請輸入股票代號"; return; }
        if (!ParseHelpers.TryParseDecimal(AddTargetPrice, out var price) || price <= 0)
        { AddError = "價格無效"; return; }

        var symbol = AddSymbol.Trim().ToUpper();
        var exchange = _search.GetExchange(symbol);
        if (exchange is null)
        { AddError = "找不到股票代號"; return; }

        var condition = AddCondition == "突破" ? AlertCondition.Above : AlertCondition.Below;
        var rule = new AlertRule(Guid.NewGuid(), symbol, exchange, condition, price);
        await _repo.AddAsync(rule);

        Rules.Add(ToRow(rule));
        HasNoRules = false;

        AddSymbol = string.Empty;
        AddTargetPrice = string.Empty;
        var condLabel = condition == AlertCondition.Above
            ? GetString("Alerts.ConditionAbove", "突破")
            : GetString("Alerts.ConditionBelow", "跌破");
        _snackbar.Success(string.Format(
            GetString("Alerts.Added", "已新增警示：{0} {1} {2}"),
            symbol, condLabel, price.ToString("0.##")));
    }

    [RelayCommand]
    private async Task SaveRule(AlertRowViewModel row)
    {
        row.EditError = string.Empty;
        if (!ParseHelpers.TryParseDecimal(row.EditTargetPrice, out var price) || price <= 0)
        {
            row.EditError = "價格無效";
            return;
        }
        var condition = row.EditCondition == "突破" ? AlertCondition.Above : AlertCondition.Below;

        // Reset trigger state since rule changed — decrement badge if it was triggered
        if (row.IsTriggered)
            TriggeredCount = Math.Max(0, TriggeredCount - 1);

        row.TargetPrice = price;
        row.Condition = condition;
        row.IsTriggered = false;
        row.TriggeredAt = string.Empty;
        row.IsEditing = false;

        await _repo.UpdateAsync(row.ToRule());
        _snackbar.Success(string.Format(GetString("Alerts.Updated", "已更新 {0} 警示規則"), row.Symbol));
    }

    [RelayCommand]
    private async Task RemoveRule(Guid id)
    {
        await _repo.RemoveAsync(id);
        var row = Rules.FirstOrDefault(r => r.Id == id);
        if (row is not null)
        {
            if (row.IsTriggered)
                TriggeredCount = Math.Max(0, TriggeredCount - 1);
            Rules.Remove(row);
        }
        HasNoRules = Rules.Count == 0;
    }

    private void CheckAlerts(IReadOnlyList<StockQuote> quotes)
    {
        foreach (var quote in quotes)
        {
            foreach (var row in Rules.Where(r => r.Symbol == quote.Symbol && !r.IsTriggered))
            {
                row.CurrentPrice = quote.Price;

                bool triggered = row.Condition == AlertCondition.Above
                    ? quote.Price >= row.TargetPrice
                    : quote.Price <= row.TargetPrice;

                if (triggered)
                {
                    row.IsTriggered = true;
                    row.TriggeredAt = DateTimeOffset.Now.ToLocalTime().ToString("HH:mm:ss");
                    _ = _repo.UpdateAsync(row.ToRule());

                    TriggeredCount++;

                    var condLabel = row.Condition == AlertCondition.Above
                        ? GetString("Alerts.ConditionAbove", "突破")
                        : GetString("Alerts.ConditionBelow", "跌破");
                    var name = string.IsNullOrWhiteSpace(row.Name) ? string.Empty : $" {row.Name}";
                    _snackbar.Warning(string.Format(
                        GetString("Alerts.ToastMessage", "🔔 {0}{1}：{2} {3}"),
                        row.Symbol, name, condLabel, row.TargetPrice.ToString("0.##")));
                }
            }
        }
    }

    private AlertRowViewModel ToRow(AlertRule r) => new()
    {
        Id = r.Id,
        Symbol = r.Symbol,
        Exchange = r.Exchange,
        Name = _search.GetName(r.Symbol) ?? string.Empty,
        Condition = r.Condition,
        TargetPrice = r.TargetPrice,
        IsTriggered = r.IsTriggered,
        TriggeredAt = r.TriggerTime.HasValue
            ? r.TriggerTime.Value.ToLocalTime().ToString("HH:mm:ss")
            : string.Empty,
    };

    private void OnCurrencyChanged()
    {
        foreach (var rule in Rules)
            rule.NotifyCurrencyChanged();
    }

    public void Dispose()
    {
        if (_currencyService is not null)
            _currencyService.CurrencyChanged -= OnCurrencyChanged;
        _subscription?.Dispose();
    }

    private string GetString(string key, string fallback) =>
        _localization.Get(key, fallback);
}
