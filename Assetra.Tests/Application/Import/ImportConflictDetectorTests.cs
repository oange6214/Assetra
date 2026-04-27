using Assetra.Application.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Import;

public class ImportConflictDetectorTests
{
    [Fact]
    public async Task FlagsRow_WhenMatchingDateAmountAndSymbolExists()
    {
        var existingTrade = NewBuyTrade(new DateTime(2026, 4, 26), price: 910m, qty: 1000, symbol: "2330");
        var repo = MockRepo(existingTrade);

        var newRow = new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910000m,
            "買進", null, Symbol: "2330", Quantity: 1000m);
        var batch = NewBatch(newRow);

        var detector = new ImportConflictDetector(repo.Object);
        var result = await detector.DetectAsync(batch);

        Assert.Single(result.Conflicts);
        Assert.Equal(existingTrade.Id, result.Conflicts[0].ExistingTradeId);
        Assert.Equal(ImportBatchStatus.Previewing, result.Status);
    }

    [Fact]
    public async Task NoConflict_WhenSymbolDiffers()
    {
        var existing = NewBuyTrade(new DateTime(2026, 4, 26), 910m, 1000, "2330");
        var repo = MockRepo(existing);

        var batch = NewBatch(new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910000m,
            "買進", null, Symbol: "2317", Quantity: 1000m));

        var result = await new ImportConflictDetector(repo.Object).DetectAsync(batch);

        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public async Task NoConflict_WhenBrokerQuantityDiffers()
    {
        var existing = NewBuyTrade(new DateTime(2026, 4, 26), 910m, 1000, "2330");
        var repo = MockRepo(existing);

        var batch = NewBatch(new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910000m,
            "買進", null, Symbol: "2330", Quantity: 999m));

        var result = await new ImportConflictDetector(repo.Object).DetectAsync(batch);

        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public async Task NoConflict_WhenBrokerDirectionDiffers()
    {
        var existing = NewBuyTrade(new DateTime(2026, 4, 26), 910m, 1000, "2330");
        var repo = MockRepo(existing);

        var batch = NewBatch(new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910000m,
            "賣出", null, Symbol: "2330", Quantity: 1000m));

        var result = await new ImportConflictDetector(repo.Object).DetectAsync(batch);

        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public async Task FlagsRow_ForBankIncomeMatchingExistingIncome()
    {
        var existing = NewIncomeTrade(new DateTime(2026, 4, 27), amount: 1500m);
        var repo = MockRepo(existing);

        var batch = new ImportBatch(
            Guid.NewGuid(), "statement.csv", ImportFileType.Csv, ImportFormat.CathayUnitedBank,
            DateTimeOffset.UtcNow,
            new[] { new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 1500m, "薪資", "四月") },
            Array.Empty<ImportConflict>());

        var result = await new ImportConflictDetector(repo.Object).DetectAsync(batch);

        Assert.Single(result.Conflicts);
        Assert.Equal(existing.Id, result.Conflicts[0].ExistingTradeId);
    }

    private static Mock<Assetra.Core.Interfaces.ITradeRepository> MockRepo(params Trade[] existing)
    {
        var mock = new Mock<Assetra.Core.Interfaces.ITradeRepository>();
        mock.Setup(r => r.GetAllAsync()).ReturnsAsync(existing);
        return mock;
    }

    private static Trade NewBuyTrade(DateTime date, decimal price, int qty, string symbol) =>
        new(Guid.NewGuid(), symbol, "TWSE", symbol, TradeType.Buy, date,
            price, qty, null, null);

    private static Trade NewIncomeTrade(DateTime date, decimal amount) =>
        new(Guid.NewGuid(), string.Empty, string.Empty, string.Empty, TradeType.Income, date,
            0m, 1, null, null, CashAmount: amount);

    private static ImportBatch NewBatch(params ImportPreviewRow[] rows) =>
        new(Guid.NewGuid(), "trades.csv", ImportFileType.Csv, ImportFormat.YuantaSecurities,
            DateTimeOffset.UtcNow, rows, Array.Empty<ImportConflict>());
}
