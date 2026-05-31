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
    private readonly ISymbolDirectory? _symbolDirectory;
    private IDisposable? _subscription;

    private readonly ObservableCollection<AlertRowViewModel> _rules = [];
    public ReadOnlyObservableCollection<AlertRowViewModel> Rules { get; }

    // Add form
    [ObservableProperty] private string _addSymbol = string.Empty;
    [ObservableProperty] private string _addExchange = string.Empty;
    [ObservableProperty] private string _addSymbolName = string.Empty;
    [ObservableProperty] private string _addTargetPrice = string.Empty;
    [ObservableProperty] private string _addCondition = "突破";
    [ObservableProperty] private string _addError = string.Empty;
    [ObservableProperty] private bool _hasNoRules = true;
    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private bool _isDeleteConfirmOpen;
    [ObservableProperty] private string _deleteTargetName = string.Empty;
    [ObservableProperty] private bool _isSuggestionsOpen;
    [ObservableProperty] private StockSearchResult? _selectedSuggestion;
    [ObservableProperty] private IReadOnlyList<StockSearchResult> _symbolSuggestions = [];
    private AlertRowViewModel? _pendingDelete;

    // NavRail badge
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTriggeredAlerts))]
    [NotifyPropertyChangedFor(nameof(TriggeredBadge))]
    private int _triggeredCount;

    public bool HasTriggeredAlerts => TriggeredCount > 0;
    public string TriggeredBadge => TriggeredCount > 99 ? "99+" : TriggeredCount.ToString();
    public int AlertCount => Rules.Count;
    public int MonitoringCount => Rules.Count(r => !r.IsTriggered);

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

        AddExchange = string.Empty;
        AddSymbolName = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            IsSuggestionsOpen = false;
            SymbolSuggestions = [];
            return;
        }

        SymbolSuggestions = _symbolDirectory?.Search(value.Trim()) ?? _search.Search(value.Trim());
        IsSuggestionsOpen = SymbolSuggestions.Count > 0;
    }

    public AlertsViewModel(
        IAlertService alertRepo,
        IStockSearchService search,
        IStockService stockService,
        IScheduler uiScheduler,
        ISnackbarService snackbar,
        ILocalizationService localization,
        ICurrencyService? currencyService = null,
        ISymbolDirectory? symbolDirectory = null)
    {
        _alertService = alertRepo;
        _search = search;
        _snackbar = snackbar;
        _localization = localization;
        _currencyService = currencyService;
        _symbolDirectory = symbolDirectory;
        Rules = new ReadOnlyObservableCollection<AlertRowViewModel>(_rules);

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
        _rules.Clear();
        foreach (var r in rules)
            _rules.Add(ToRow(r));
        HasNoRules = Rules.Count == 0;
        TriggeredCount = Rules.Count(r => r.IsTriggered);
        NotifyRuleCountsChanged();
    }

    [RelayCommand]
    private void OpenAddForm()
    {
        AddError = string.Empty;
        IsSuggestionsOpen = false;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CancelAddForm()
    {
        AddError = string.Empty;
        IsSuggestionsOpen = false;
        SymbolSuggestions = [];
        AddSymbol = string.Empty;
        AddExchange = string.Empty;
        AddSymbolName = string.Empty;
        AddTargetPrice = string.Empty;
        AddCondition = "突破";
        IsFormOpen = false;
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
        var resolved = ResolveSymbol(symbol, AddExchange);
        var exchange = string.IsNullOrWhiteSpace(AddExchange)
            ? resolved?.Exchange ?? _search.GetExchange(symbol)
            : AddExchange.Trim().ToUpperInvariant();
        if (exchange is null)
        { AddError = "找不到股票代號"; return; }

        var condition = AddCondition == "突破" ? AlertCondition.Above : AlertCondition.Below;
        var rule = new AlertRule(Guid.NewGuid(), symbol, exchange, condition, price);
        await _alertService.AddAsync(rule);

        _rules.Add(ToRow(rule));
        HasNoRules = false;
        NotifyRuleCountsChanged();

        IsSuggestionsOpen = false;
        SymbolSuggestions = [];
        AddSymbol = string.Empty;
        AddExchange = string.Empty;
        AddSymbolName = string.Empty;
        AddTargetPrice = string.Empty;
        IsFormOpen = false;
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
        NotifyRuleCountsChanged();

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
            _rules.Remove(row);
        }
        HasNoRules = Rules.Count == 0;
        NotifyRuleCountsChanged();
    }

    [RelayCommand]
    private void RequestRemoveRule(AlertRowViewModel row)
    {
        if (row is null)
            return;
        _pendingDelete = row;
        DeleteTargetName = $"{row.Symbol} {row.ConditionText}";
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        var row = _pendingDelete;
        CancelDelete();
        if (row is not null)
            await RemoveRule(row.Id).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDelete = null;
        DeleteTargetName = string.Empty;
        IsDeleteConfirmOpen = false;
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
                    NotifyRuleCountsChanged();

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
        Name = ResolveSymbol(r.Symbol, r.Exchange)?.Name ?? _search.GetName(r.Symbol) ?? string.Empty,
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

    private void NotifyRuleCountsChanged()
    {
        OnPropertyChanged(nameof(AlertCount));
        OnPropertyChanged(nameof(MonitoringCount));
    }

    private bool _suppressSuggestions;

    private void SelectSuggestion(StockSearchResult suggestion)
    {
        _suppressSuggestions = true;
        IsSuggestionsOpen = false;
        AddSymbol = suggestion.Symbol;
        AddExchange = suggestion.Exchange;
        AddSymbolName = suggestion.Name;
        _suppressSuggestions = false;
    }

    private StockSearchResult? ResolveSymbol(string symbol, string? exchange = null) =>
        _symbolDirectory?.Resolve(symbol, exchange);
}
