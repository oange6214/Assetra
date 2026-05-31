using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class AddAssetWorkflowServiceTests
{
    [Fact]
    public void SearchSymbols_UsesCompositeSymbolDirectory()
    {
        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.Search("AAPL")).Returns([]);
        var directory = new FakeSymbolDirectory([
            new StockSearchResult("AAPL", "Apple Inc.", "NASDAQ", Currency: "USD"),
        ]);
        var service = new AddAssetWorkflowService(search.Object, symbolDirectory: directory);

        var results = service.SearchSymbols("AAPL");

        var result = Assert.Single(results);
        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal("NASDAQ", result.Exchange);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void BuildBuyPreview_UsSymbol_DoesNotApplyTaiwanBuyCommission()
    {
        var search = new Mock<IStockSearchService>();
        var directory = new FakeSymbolDirectory([
            new StockSearchResult("AAPL", "Apple Inc.", "NASDAQ", Currency: "USD"),
        ]);
        var service = new AddAssetWorkflowService(search.Object, symbolDirectory: directory);

        var preview = service.BuildBuyPreview(new BuyPreviewRequest(
            "AAPL",
            Price: 100m,
            Quantity: 10,
            CommissionDiscount: 1m,
            ManualFee: null,
            Exchange: "NASDAQ"));

        Assert.Equal(1_000m, preview.GrossAmount);
        Assert.Equal(0m, preview.Commission);
        Assert.Equal(1_000m, preview.TotalCost);
    }

    [Fact]
    public async Task LookupClosePriceAsync_UsesDirectoryExchangeForUsSymbol()
    {
        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetExchange("AAPL")).Returns((string?)null);
        var history = new FakeHistoryProvider([
            new OhlcvPoint(new DateOnly(2026, 5, 8), 180m, 190m, 179m, 188m, 100_000),
        ]);
        var directory = new FakeSymbolDirectory([
            new StockSearchResult("AAPL", "Apple Inc.", "NASDAQ", Currency: "USD"),
        ]);
        var service = new AddAssetWorkflowService(
            search.Object,
            historyProvider: history,
            symbolDirectory: directory);

        var result = await service.LookupClosePriceAsync("AAPL", new DateTime(2026, 5, 8));

        Assert.True(result.HasPrice);
        Assert.Equal(188m, result.Price);
        Assert.Equal("NASDAQ", history.CapturedExchange);
    }

    [Fact]
    public async Task EnsureStockEntryAsync_UsesDirectoryExchangeNameAndCurrency()
    {
        var search = new Mock<IStockSearchService>();
        var portfolioRepo = new Mock<IPortfolioRepository>();
        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        var txService = new Mock<ITransactionService>();
        var directory = new FakeSymbolDirectory([
            new StockSearchResult("AAPL", "Apple Inc.", "NASDAQ", Currency: "USD"),
        ]);

        string? capturedSymbol = null;
        string? capturedExchange = null;
        string? capturedName = null;
        string? capturedCurrency = null;
        bool capturedIsEtf = true;
        var entryId = Guid.NewGuid();
        portfolioRepo
            .Setup(r => r.FindOrCreatePortfolioEntryAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<AssetType>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, AssetType, string?, bool, Guid?, CancellationToken>((symbol, exchange, name, _, currency, isEtf, _, _) =>
            {
                capturedSymbol = symbol;
                capturedExchange = exchange;
                capturedName = name;
                capturedCurrency = currency;
                capturedIsEtf = isEtf;
            })
            .ReturnsAsync(entryId);
        portfolioRepo.Setup(r => r.UnarchiveAsync(entryId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new AddAssetWorkflowService(
            search.Object,
            portfolioRepository: portfolioRepo.Object,
            positionLogRepository: logRepo.Object,
            transactionService: txService.Object,
            symbolDirectory: directory);

        var entry = await service.EnsureStockEntryAsync(new EnsureStockEntryRequest("AAPL"));

        Assert.Equal(entryId, entry.Id);
        Assert.Equal("AAPL", capturedSymbol);
        Assert.Equal("NASDAQ", capturedExchange);
        Assert.Equal("Apple Inc.", capturedName);
        Assert.Equal("USD", capturedCurrency);
        Assert.False(capturedIsEtf);
        Assert.Equal("USD", entry.Currency);
    }

    [Fact]
    public async Task ExecuteStockBuyAsync_RecordsActualCashAmountWithoutChangingTradePrice()
    {
        var search = new Mock<IStockSearchService>();
        var portfolioRepo = new Mock<IPortfolioRepository>();
        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        var txService = new Mock<ITransactionService>();
        var directory = new FakeSymbolDirectory([
            new StockSearchResult("AAPL", "Apple Inc.", "NASDAQ", Currency: "USD"),
        ]);
        var entryId = Guid.NewGuid();
        Trade? recorded = null;

        portfolioRepo
            .Setup(r => r.FindOrCreatePortfolioEntryAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<AssetType>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entryId);
        portfolioRepo.Setup(r => r.UnarchiveAsync(entryId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        logRepo.Setup(r => r.LogAsync(It.IsAny<PortfolioPositionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        txService.Setup(s => s.RecordAsync(It.IsAny<Trade>()))
            .Callback<Trade>(t => recorded = t)
            .Returns(Task.CompletedTask);

        var service = new AddAssetWorkflowService(
            search.Object,
            portfolioRepository: portfolioRepo.Object,
            positionLogRepository: logRepo.Object,
            transactionService: txService.Object,
            symbolDirectory: directory);

        await service.ExecuteStockBuyAsync(new StockBuyRequest(
            Symbol: "AAPL",
            Price: 188.25m,
            Quantity: 10,
            BuyDate: new DateTime(2026, 5, 8),
            CashAccountId: Guid.NewGuid(),
            CommissionDiscount: 1m,
            ManualFee: null,
            ActualCashAmount: 58_420m));

        Assert.NotNull(recorded);
        Assert.Equal(188.25m, recorded!.Price);
        Assert.Equal(10, recorded.Quantity);
        Assert.Equal(58_420m, recorded.CashAmount);
        Assert.Equal("NASDAQ", recorded.Exchange);
    }

    [Fact]
    public async Task ExecuteStockBuyAsync_RecordsSettlementFxAuditMetadata()
    {
        var search = new Mock<IStockSearchService>();
        var portfolioRepo = new Mock<IPortfolioRepository>();
        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        var txService = new Mock<ITransactionService>();
        var directory = new FakeSymbolDirectory([
            new StockSearchResult("DRAM", "Roundhill Memory ETF", "NASDAQ", Currency: "USD"),
        ]);
        var entryId = Guid.NewGuid();
        Trade? recorded = null;

        portfolioRepo
            .Setup(r => r.FindOrCreatePortfolioEntryAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<AssetType>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entryId);
        portfolioRepo.Setup(r => r.UnarchiveAsync(entryId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        logRepo.Setup(r => r.LogAsync(It.IsAny<PortfolioPositionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        txService.Setup(s => s.RecordAsync(It.IsAny<Trade>()))
            .Callback<Trade>(t => recorded = t)
            .Returns(Task.CompletedTask);

        var service = new AddAssetWorkflowService(
            search.Object,
            portfolioRepository: portfolioRepo.Object,
            positionLogRepository: logRepo.Object,
            transactionService: txService.Object,
            symbolDirectory: directory);

        await service.ExecuteStockBuyAsync(new StockBuyRequest(
            Symbol: "DRAM",
            Price: 51.23m,
            Quantity: 20,
            BuyDate: new DateTime(2026, 5, 8),
            CashAccountId: Guid.NewGuid(),
            CommissionDiscount: 1m,
            ManualFee: null,
            ActualCashAmount: 33_128m,
            FxRate: 32.335m,
            SettlementCurrency: "TWD",
            FxRateDate: new DateOnly(2026, 5, 8),
            FxSource: "Frankfurter"));

        Assert.NotNull(recorded);
        Assert.Equal("USD", recorded!.InstrumentCurrency);
        Assert.Equal("TWD", recorded.SettlementCurrency);
        Assert.Equal(32.335m, recorded.FxRate);
        Assert.Equal(new DateOnly(2026, 5, 8), recorded.FxRateDate);
        Assert.Equal("Frankfurter", recorded.FxSource);
    }

    [Fact]
    public async Task CreateManualAssetAsync_PersistsEntry_AndReturnsSnapshot()
    {
        var search = new Mock<IStockSearchService>();
        var portfolioRepo = new Mock<IPortfolioRepository>();
        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        var txService = new Mock<ITransactionService>();

        PortfolioEntry? addedEntry = null;
        portfolioRepo.Setup(r => r.AddAsync(It.IsAny<PortfolioEntry>()))
            .Callback<PortfolioEntry, CancellationToken>((e, _) => addedEntry = e)
            .Returns(Task.CompletedTask);

        var service = new AddAssetWorkflowService(
            search.Object,
            portfolioRepository: portfolioRepo.Object,
            positionLogRepository: logRepo.Object,
            transactionService: txService.Object);

        var result = await service.CreateManualAssetAsync(new ManualAssetCreateRequest(
            Symbol: "BTC",
            Exchange: string.Empty,
            Name: "BTC",
            AssetType: AssetType.Crypto,
            Quantity: 0.5m,
            TotalCost: 1_000_000m,
            UnitPrice: 2_000_000m,
            AcquiredOn: new DateOnly(2026, 4, 21)));

        Assert.NotNull(addedEntry);
        Assert.Equal(AssetType.Crypto, addedEntry!.AssetType);
        Assert.Equal("BTC", addedEntry.Symbol);
        Assert.Equal(addedEntry.Id, result.Snapshot.PortfolioEntryId);
        Assert.Equal(0.5m, result.Snapshot.Quantity);
        Assert.Equal(1_000_000m, result.Snapshot.TotalCost);
        Assert.Equal(2_000_000m, result.Snapshot.AverageCost);
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

    private sealed class FakeHistoryProvider(IReadOnlyList<OhlcvPoint> points) : IStockHistoryProvider
    {
        public string? CapturedExchange { get; private set; }

        public Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
            string symbol,
            string exchange,
            ChartPeriod period,
            CancellationToken ct = default)
        {
            CapturedExchange = exchange;
            return Task.FromResult(points);
        }
    }
}
