using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using Assetra.Application.Alerts.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Alerts;
using Assetra.WPF.Infrastructure;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class AlertsViewModelTests
{
    [Fact]
    public async Task LoadAsync_LeavesCurrentPriceEmptyUntilQuoteArrives()
    {
        var quoteStream = new Subject<IReadOnlyList<StockQuote>>();
        var vm = CreateViewModel(
            quoteStream,
            out _,
            new AlertRule(Guid.NewGuid(), "2330", "TWSE", AlertCondition.Above, 900m));

        await vm.LoadAsync();

        Assert.Null(vm.Rules.Single().CurrentPrice);
    }

    [Fact]
    public async Task QuoteWithDifferentExchange_DoesNotTriggerRuleWithSameSymbol()
    {
        var quoteStream = new Subject<IReadOnlyList<StockQuote>>();
        var vm = CreateViewModel(
            quoteStream,
            out var alertService,
            new AlertRule(Guid.NewGuid(), "1234", "TPEX", AlertCondition.Above, 50m));

        await vm.LoadAsync();

        quoteStream.OnNext([Quote("1234", "TWSE", 60m)]);
        await Task.Delay(50);

        Assert.Null(vm.Rules.Single().CurrentPrice);
        Assert.False(vm.Rules.Single().IsTriggered);
        alertService.Verify(
            s => s.UpdateAsync(It.IsAny<AlertRule>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task QuoteWithSameExchange_PersistsTriggeredRule()
    {
        var quoteStream = new Subject<IReadOnlyList<StockQuote>>();
        AlertRule? saved = null;
        var vm = CreateViewModel(
            quoteStream,
            out var alertService,
            new AlertRule(Guid.NewGuid(), "2330", "TWSE", AlertCondition.Above, 900m));
        alertService
            .Setup(s => s.UpdateAsync(It.IsAny<AlertRule>(), It.IsAny<CancellationToken>()))
            .Callback<AlertRule, CancellationToken>((rule, _) => saved = rule)
            .Returns(Task.CompletedTask);

        await vm.LoadAsync();

        quoteStream.OnNext([Quote("2330", "TWSE", 910m)]);
        await WaitForAsync(() => saved is not null);

        var row = vm.Rules.Single();
        Assert.Equal(910m, row.CurrentPrice);
        Assert.True(row.IsTriggered);
        Assert.Equal(1, vm.TriggeredCount);
        Assert.NotNull(saved!.TriggerTime);
        Assert.Equal("TWSE", saved.Exchange);
    }

    [Fact]
    public async Task TriggerPersistFailure_RevertsTriggeredState()
    {
        var quoteStream = new Subject<IReadOnlyList<StockQuote>>();
        var snackbar = new FakeSnackbarService();
        var vm = CreateViewModel(
            quoteStream,
            out var alertService,
            new AlertRule(Guid.NewGuid(), "2330", "TWSE", AlertCondition.Above, 900m),
            snackbar);
        alertService
            .Setup(s => s.UpdateAsync(It.IsAny<AlertRule>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException(new InvalidOperationException("db failed")));

        await vm.LoadAsync();

        quoteStream.OnNext([Quote("2330", "TWSE", 910m)]);
        await WaitForAsync(() => snackbar.LastError is not null);

        var row = vm.Rules.Single();
        Assert.Equal(910m, row.CurrentPrice);
        Assert.False(row.IsTriggered);
        Assert.Equal(0, vm.TriggeredCount);
        Assert.NotNull(snackbar.LastError);
    }

    [Fact]
    public async Task AddRule_UsesSymbolDirectoryForUsExchange()
    {
        var quoteStream = new Subject<IReadOnlyList<StockQuote>>();
        var directory = new FakeSymbolDirectory([
            new StockSearchResult("AAPL", "Apple Inc.", "NASDAQ", Currency: "USD"),
        ]);
        AlertRule? added = null;
        var vm = CreateViewModel(
            quoteStream,
            out var alertService,
            null,
            symbolDirectory: directory);
        alertService
            .Setup(s => s.AddAsync(It.IsAny<AlertRule>(), It.IsAny<CancellationToken>()))
            .Callback<AlertRule, CancellationToken>((rule, _) => added = rule)
            .Returns(Task.CompletedTask);

        vm.AddSymbol = "AAPL";
        vm.AddTargetPrice = "200";

        await vm.AddRuleCommand.ExecuteAsync(null);

        Assert.NotNull(added);
        Assert.Equal("AAPL", added!.Symbol);
        Assert.Equal("NASDAQ", added.Exchange);
        var row = Assert.Single(vm.Rules);
        Assert.Equal("Apple Inc.", row.Name);
        Assert.Equal("NASDAQ", row.Exchange);
    }

    private static AlertsViewModel CreateViewModel(
        Subject<IReadOnlyList<StockQuote>> quoteStream,
        out Mock<IAlertService> alertService,
        AlertRule? rule,
        ISnackbarService? snackbar = null,
        ISymbolDirectory? symbolDirectory = null)
    {
        alertService = new Mock<IAlertService>();
        alertService
            .Setup(s => s.GetRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rule is null ? [] : [rule]);
        alertService
            .Setup(s => s.AddAsync(It.IsAny<AlertRule>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        alertService
            .Setup(s => s.UpdateAsync(It.IsAny<AlertRule>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetName(It.IsAny<string>())).Returns("台積電");
        search.Setup(s => s.Search(It.IsAny<string>())).Returns([]);

        return new AlertsViewModel(
            alertService.Object,
            search.Object,
            new FakeStockService(quoteStream),
            ImmediateScheduler.Instance,
            snackbar ?? new FakeSnackbarService(),
            new FakeLocalizationService(),
            symbolDirectory: symbolDirectory);
    }

    private static StockQuote Quote(string symbol, string exchange, decimal price) =>
        new(symbol, "Name", exchange, price, 0m, 0m, 0, price, price, price, price, DateTimeOffset.UnixEpoch);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    private sealed class FakeStockService(Subject<IReadOnlyList<StockQuote>> quoteStream) : IStockService
    {
        public IObservable<IReadOnlyList<StockQuote>> QuoteStream => quoteStream;
        public void Start() { }
        public void Stop() { }
        public Task RefreshNowAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() => quoteStream.Dispose();
    }

    private sealed class FakeSnackbarService : ISnackbarService
    {
        public string? LastError { get; private set; }

        public void Show(string message, SnackbarKind kind = SnackbarKind.Info) { }
        public void Show(string message, string actionLabel, Action onAction, SnackbarKind kind = SnackbarKind.Success) { }
        public void Success(string message) { }
        public void Warning(string message) { }
        public void Error(string message) => LastError = message;
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public string CurrentLanguage => "zh-TW";
        public event EventHandler? LanguageChanged;
        public string Get(string key, string fallback = "") => fallback;
        public void SetLanguage(string languageCode) => LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeSymbolDirectory(IReadOnlyList<StockSearchResult> results) : ISymbolDirectory
    {
        public IReadOnlyList<StockSearchResult> Search(string query) =>
            results
                .Where(r => r.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            r.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        public StockSearchResult? Resolve(string symbol, string? exchange = null) =>
            results.FirstOrDefault(r =>
                EquitySymbolNormalizer.SymbolMatches(r.Symbol, symbol) &&
                (string.IsNullOrWhiteSpace(exchange) ||
                 string.Equals(r.Exchange, exchange, StringComparison.OrdinalIgnoreCase)));
    }
}
