using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Assetra.Application.Alerts.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Assetra.WPF.Features.Alerts;

public partial class AlertsViewModel : ObservableObject, IDisposable
{
    private readonly IAlertService _alertService;
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
    [ObservableProperty] private bool _isSuggestionsOpen;
    [ObservableProperty] private StockSearchResult? _selectedSuggestion;
    [ObservableProperty] private IReadOnlyList<StockSearchResult> _symbolSuggestions = [];

    // NavRail badge
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTriggeredAlerts))]
    [NotifyPropertyChangedFor(nameof(TriggeredBadge))]
    private int _triggeredCount;

    public bool HasTriggeredAlerts => TriggeredCount > 0;
    public string TriggeredBadge => TriggeredCount > 99 ? "99+" : TriggeredCount.ToString();

    public IReadOnlyList<string> Conditions { get; } = ["突破", "跌破"];

    partial void OnSelectedSuggestionChanged(StockSearchResult? value)
    {
        if (value is null)
            return;

        SelectSuggestion(value);
        SelectedSuggestion = null;
    }

    partial void OnAddSymbolChanged(string value)
    {
        if (_suppressSuggestions)
            return;

        if (string.IsNullOrWhiteSpace(value))
        {
            IsSuggestionsOpen = false;
            SymbolSuggestions = [];
            return;
        }

        SymbolSuggestions = _search.Search(value.Trim());
        IsSuggestionsOpen = SymbolSuggestions.Count > 0;
    }

    public AlertsViewModel(
        IAlertService alertRepo,
        IStockSearchService search,
        IStockService stockService,
        IScheduler uiScheduler,
        ISnackbarService snackbar,
        ILocalizationService localization,
        ICurrencyService? currencyService = null)
    {
        _alertService = alertRepo;
        _search = search;
        _snackbar = snackbar;
        _localization = localization;
        _currencyService = currencyService;

        _subscription = stockService.QuoteStream
            .ObserveOn(uiScheduler)
            .Select(quotes => Observable.FromAsync(() => CheckAlertsAsync(quotes))
                .Catch((Exception ex) =>
                {
                    Log.Warning(ex, "Failed to process stock quotes for alerts");
                    return Observable.Empty<System.Reactive.Unit>();
                }))
            .Concat()
            .Subscribe();

        if (currencyService is not null)
            currencyService.CurrencyChanged += OnCurrencyChanged;
    }

    public async Task LoadAsync()
    {
        var rules = await _alertService.GetRulesAsync();
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
        await _alertService.AddAsync(rule);

        Rules.Add(ToRow(rule));
        HasNoRules = false;

        IsSuggestionsOpen = false;
        SymbolSuggestions = [];
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
        row.TriggerTime = null;
        row.TriggeredAt = string.Empty;
        row.IsEditing = false;

        await _alertService.UpdateAsync(row.ToRule());
        _snackbar.Success(string.Format(GetString("Alerts.Updated", "已更新 {0} 警示規則"), row.Symbol));
    }

    [RelayCommand]
    private async Task RemoveRule(Guid id)
    {
        await _alertService.RemoveAsync(id);
        var row = Rules.FirstOrDefault(r => r.Id == id);
        if (row is not null)
        {
            if (row.IsTriggered)
                TriggeredCount = Math.Max(0, TriggeredCount - 1);
            Rules.Remove(row);
        }
        HasNoRules = Rules.Count == 0;
    }

    private async Task CheckAlertsAsync(IReadOnlyList<StockQuote> quotes)
    {
        foreach (var quote in quotes)
        {
            foreach (var row in Rules.Where(r => IsSameQuoteKey(r, quote)))
            {
                row.CurrentPrice = quote.Price;

                if (row.IsTriggered)
                    continue;

                bool triggered = row.Condition == AlertCondition.Above
                    ? quote.Price >= row.TargetPrice
                    : quote.Price <= row.TargetPrice;

                if (triggered)
                {
                    var previousTriggerTime = row.TriggerTime;
                    var previousTriggeredAt = row.TriggeredAt;
                    var triggerTime = DateTimeOffset.Now;
                    row.IsTriggered = true;
                    row.TriggerTime = triggerTime;
                    row.TriggeredAt = triggerTime.ToLocalTime().ToString("HH:mm:ss");

                    try
                    {
                        await _alertService.UpdateAsync(row.ToRule());
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex,
                            "Failed to persist triggered alert {Symbol} on {Exchange}",
                            row.Symbol,
                            row.Exchange);
                        row.IsTriggered = false;
                        row.TriggerTime = previousTriggerTime;
                        row.TriggeredAt = previousTriggeredAt;
                        _snackbar.Error(string.Format(
                            GetString("Alerts.TriggerPersistFailed", "警示已觸發，但狀態儲存失敗：{0}"),
                            row.Symbol));
                        continue;
                    }

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

    private static bool IsSameQuoteKey(AlertRowViewModel row, StockQuote quote) =>
        string.Equals(row.Symbol, quote.Symbol, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Exchange, quote.Exchange, StringComparison.OrdinalIgnoreCase);

    private AlertRowViewModel ToRow(AlertRule r) => new()
    {
        Id = r.Id,
        Symbol = r.Symbol,
        Exchange = r.Exchange,
        Name = _search.GetName(r.Symbol) ?? string.Empty,
        Condition = r.Condition,
        TargetPrice = r.TargetPrice,
        IsTriggered = r.IsTriggered,
        TriggerTime = r.TriggerTime,
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

    private bool _suppressSuggestions;

    private void SelectSuggestion(StockSearchResult suggestion)
    {
        _suppressSuggestions = true;
        IsSuggestionsOpen = false;
        AddSymbol = suggestion.Symbol;
        _suppressSuggestions = false;
    }
}
