using Assetra.Application.Import;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Import;

public class ImportApplyServiceTests
{
    [Fact]
    public async Task BankRow_PositiveAmount_CreatesIncomeTrade()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var batch = BankBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 1500m, "薪資", "四月"));

        var result = await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(1, result.RowsApplied);
        Assert.Single(added);
        Assert.Equal(TradeType.Income, added[0].Type);
        Assert.Equal(1500m, added[0].CashAmount);
        Assert.Contains("薪資", added[0].Note);
    }

    [Fact]
    public async Task BankRow_NegativeAmount_CreatesWithdrawalWithAbsoluteCash()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var batch = BankBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 26), -250m, "Starbucks", "Latte"));

        await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(TradeType.Withdrawal, added[0].Type);
        Assert.Equal(250m, added[0].CashAmount);
    }

    [Fact]
    public async Task BrokerRow_BuyKeyword_CreatesBuyTradeWithDerivedPrice()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var batch = BrokerBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910000m, "買進", null,
                Symbol: "2330", Quantity: 1000m));

        await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions(Exchange: "TWSE"));

        Assert.Single(added);
        Assert.Equal(TradeType.Buy, added[0].Type);
        Assert.Equal("2330", added[0].Symbol);
        Assert.Equal(1000, added[0].Quantity);
        Assert.Equal(910m, added[0].Price);
    }

    [Fact]
    public async Task BrokerRow_PreservesExplicitUnitPriceAndCommission()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var batch = BrokerBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910425m, "買進", null,
                Symbol: "2330", Quantity: 1000m, UnitPrice: 910m, Commission: 425m));

        await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions(Exchange: "TWSE"));

        Assert.Single(added);
        Assert.Equal(910m, added[0].Price);
        Assert.Equal(425m, added[0].Commission);
    }

    [Fact]
    public async Task BrokerRow_DerivesUnitPriceExcludingCommission_WhenPriceMissing()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var batch = BrokerBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910425m, "買進", null,
                Symbol: "2330", Quantity: 1000m, Commission: 425m));

        await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions(Exchange: "TWSE"));

        Assert.Single(added);
        Assert.Equal(910m, added[0].Price);
        Assert.Equal(425m, added[0].Commission);
    }

    [Fact]
    public async Task BrokerRow_SellKeyword_CreatesSellTrade()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var batch = BrokerBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910000m, "賣出", null,
                Symbol: "2330", Quantity: 1000m));

        await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(TradeType.Sell, added[0].Type);
    }

    [Fact]
    public async Task SkipsConflict_WhenResolutionIsSkip()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var row = new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 1500m, "薪資", null);
        var batch = BankBatch(row) with
        {
            Conflicts = new[] { new ImportConflict(row, Guid.NewGuid(), null, ImportConflictResolution.Skip) },
        };

        var result = await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(0, result.RowsApplied);
        Assert.Equal(1, result.RowsSkipped);
        Assert.Empty(added);
    }

    [Fact]
    public async Task OverwritesConflict_RemovesExistingThenAdds()
    {
        var added = new List<Trade>();
        var existingId = Guid.NewGuid();
        Guid? removedId = null;

        var repo = new Mock<ITradeRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<Trade>()))
            .Callback<Trade>(added.Add)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.RemoveAsync(It.IsAny<Guid>()))
            .Callback<Guid>(id => removedId = id)
            .Returns(Task.CompletedTask);

        var row = new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 1500m, "薪資", null);
        var batch = BankBatch(row) with
        {
            Conflicts = new[] { new ImportConflict(row, existingId, null, ImportConflictResolution.Overwrite) },
        };

        var result = await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(1, result.RowsApplied);
        Assert.Equal(1, result.RowsOverwritten);
        Assert.Equal(existingId, removedId);
        Assert.Single(added);
    }

    [Fact]
    public async Task BrokerRow_MissingSymbol_AddsWarningAndSkips()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var batch = BrokerBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910000m, "買進", null,
                Symbol: null, Quantity: 1000m));

        var result = await new ImportApplyService(repo.Object).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(0, result.RowsApplied);
        Assert.Equal(1, result.RowsSkipped);
        Assert.Single(result.Warnings);
        Assert.Empty(added);
    }

    private static Mock<ITradeRepository> NewRepo(List<Trade> sink)
    {
        var mock = new Mock<ITradeRepository>();
        mock.Setup(r => r.AddAsync(It.IsAny<Trade>()))
            .Callback<Trade>(sink.Add)
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static ImportBatch BankBatch(params ImportPreviewRow[] rows) =>
        new(Guid.NewGuid(), "statement.csv", ImportFileType.Csv, ImportFormat.CathayUnitedBank,
            DateTimeOffset.UtcNow, rows, Array.Empty<ImportConflict>());

    private static ImportBatch BrokerBatch(params ImportPreviewRow[] rows) =>
        new(Guid.NewGuid(), "trades.csv", ImportFileType.Csv, ImportFormat.YuantaSecurities,
            DateTimeOffset.UtcNow, rows, Array.Empty<ImportConflict>());
}
