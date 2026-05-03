using Assetra.Application.Import;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
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

        var result = await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

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

        await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

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

        await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions(Exchange: "TWSE"));

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

        await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions(Exchange: "TWSE"));

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

        await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions(Exchange: "TWSE"));

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

        await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

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

        var result = await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

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
            .Callback<Trade, CancellationToken>((t, _) => added.Add(t))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetByIdAsync(existingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Trade(existingId, "", "", "舊", TradeType.Income,
                new DateTime(2026, 4, 27), 0m, 1, null, null, CashAmount: 1m));
        repo.Setup(r => r.ApplyAtomicAsync(It.IsAny<IReadOnlyList<TradeMutation>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<TradeMutation>, CancellationToken>((mutations, _) =>
            {
                var remove = Assert.IsType<RemoveTradeMutation>(mutations[0]);
                removedId = remove.Id;
                var add = Assert.IsType<AddTradeMutation>(mutations[1]);
                added.Add(add.Trade);
            })
            .Returns(Task.CompletedTask);

        var row = new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 1500m, "薪資", null);
        var batch = BankBatch(row) with
        {
            Conflicts = new[] { new ImportConflict(row, existingId, null, ImportConflictResolution.Overwrite) },
        };

        var result = await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(1, result.RowsApplied);
        Assert.Equal(1, result.RowsOverwritten);
        Assert.Equal(existingId, removedId);
        Assert.Single(added);
    }

    [Fact]
    public async Task OverwritesConflict_RemovesBeforeAdd()
    {
        IReadOnlyList<TradeMutation>? mutations = null;
        var existingId = Guid.NewGuid();

        var repo = new Mock<ITradeRepository>();
        repo.Setup(r => r.GetByIdAsync(existingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Trade(existingId, "", "", "舊", TradeType.Income,
                new DateTime(2026, 4, 27), 0m, 1, null, null, CashAmount: 1m));
        repo.Setup(r => r.ApplyAtomicAsync(It.IsAny<IReadOnlyList<TradeMutation>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<TradeMutation>, CancellationToken>((m, _) => mutations = m)
            .Returns(Task.CompletedTask);

        var row = new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 1500m, "薪資", null);
        var batch = BankBatch(row) with
        {
            Conflicts = new[] { new ImportConflict(row, existingId, null, ImportConflictResolution.Overwrite) },
        };

        await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

        Assert.NotNull(mutations);
        Assert.Collection(mutations!,
            m => Assert.Equal(existingId, Assert.IsType<RemoveTradeMutation>(m).Id),
            m => Assert.IsType<AddTradeMutation>(m));
    }

    [Fact]
    public async Task OverwriteConflict_WithNullExistingTradeId_AddsWithoutRemove()
    {
        var added = new List<Trade>();
        var repo = new Mock<ITradeRepository>();
        var removeCalls = 0;
        repo.Setup(r => r.AddAsync(It.IsAny<Trade>()))
            .Callback<Trade, CancellationToken>((t, _) => added.Add(t))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.RemoveAsync(It.IsAny<Guid>()))
            .Callback(() => removeCalls++)
            .Returns(Task.CompletedTask);

        var row = new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 1500m, "薪資", null);
        var batch = BankBatch(row) with
        {
            Conflicts = new[] { new ImportConflict(row, ExistingTradeId: null, null, ImportConflictResolution.Overwrite) },
        };

        var result = await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(0, removeCalls);
        Assert.Equal(1, result.RowsApplied);
        Assert.Equal(0, result.RowsOverwritten);
        Assert.Single(added);
    }

    [Fact]
    public async Task Apply_MixedResolutions_ProducesCorrectCounts()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        repo.Setup(r => r.RemoveAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new Trade(id, "", "", "舊", TradeType.Income,
                new DateTime(2026, 4, 26), 0m, 1, null, null, CashAmount: 1m));

        var skipRow = new ImportPreviewRow(1, new DateOnly(2026, 4, 25), 100m, "A", null);
        var overwriteRow = new ImportPreviewRow(2, new DateOnly(2026, 4, 26), 200m, "B", null);
        var newRow = new ImportPreviewRow(3, new DateOnly(2026, 4, 27), 300m, "C", null);

        var batch = new ImportBatch(
            Guid.NewGuid(), "mixed.csv", ImportFileType.Csv, ImportFormat.CathayUnitedBank,
            DateTimeOffset.UtcNow,
            new[] { skipRow, overwriteRow, newRow },
            new[]
            {
                new ImportConflict(skipRow, Guid.NewGuid(), null, ImportConflictResolution.Skip),
                new ImportConflict(overwriteRow, Guid.NewGuid(), null, ImportConflictResolution.Overwrite),
            });

        var result = await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(3, result.RowsConsidered);
        Assert.Equal(2, result.RowsApplied);
        Assert.Equal(1, result.RowsSkipped);
        Assert.Equal(1, result.RowsOverwritten);
        Assert.Equal(2, added.Count);
    }

    [Fact]
    public async Task BrokerRow_MissingSymbol_AddsWarningAndSkips()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var batch = BrokerBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 26), 910000m, "買進", null,
                Symbol: null, Quantity: 1000m));

        var result = await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Equal(0, result.RowsApplied);
        Assert.Equal(1, result.RowsSkipped);
        Assert.Single(result.Warnings);
        Assert.Empty(added);
    }

    [Fact]
    public async Task History_NotWritten_WhenRepositoryNotInjected()
    {
        var repo = NewRepo(new List<Trade>());
        var batch = BankBatch(new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 100m, "A", null));

        var result = await new ImportApplyService(repo.Object, new ImportRowMapper()).ApplyAsync(batch, new ImportApplyOptions());

        Assert.Null(result.HistoryId);
    }

    [Fact]
    public async Task History_WritesAddedAndSkippedAndOverwrittenEntries()
    {
        var added = new List<Trade>();
        var existingId = Guid.NewGuid();
        var existingTrade = new Trade(
            existingId, "", "", "舊", TradeType.Income, new DateTime(2026, 4, 26),
            0m, 1, null, null, CashAmount: 99m);

        var repo = new Mock<ITradeRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<Trade>()))
            .Callback<Trade, CancellationToken>((t, _) => added.Add(t))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.RemoveAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.GetByIdAsync(existingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTrade);
        repo.Setup(r => r.ApplyAtomicAsync(It.IsAny<IReadOnlyList<TradeMutation>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<TradeMutation>, CancellationToken>((mutations, _) =>
            {
                foreach (var mutation in mutations)
                {
                    if (mutation is AddTradeMutation add)
                        added.Add(add.Trade);
                }
            })
            .Returns(Task.CompletedTask);

        var skipRow = new ImportPreviewRow(1, new DateOnly(2026, 4, 25), 100m, "A", null);
        var overwriteRow = new ImportPreviewRow(2, new DateOnly(2026, 4, 26), 200m, "B", null);
        var newRow = new ImportPreviewRow(3, new DateOnly(2026, 4, 27), 300m, "C", null);
        var batch = new ImportBatch(
            Guid.NewGuid(), "mixed.csv", ImportFileType.Csv, ImportFormat.CathayUnitedBank,
            DateTimeOffset.UtcNow,
            new[] { skipRow, overwriteRow, newRow },
            new[]
            {
                new ImportConflict(skipRow, Guid.NewGuid(), null, ImportConflictResolution.Skip),
                new ImportConflict(overwriteRow, existingId, null, ImportConflictResolution.Overwrite),
            });

        ImportBatchHistory? saved = null;
        var historyRepo = new Mock<IImportBatchHistoryRepository>();
        historyRepo.Setup(r => r.SaveAsync(It.IsAny<ImportBatchHistory>(), It.IsAny<CancellationToken>()))
            .Callback<ImportBatchHistory, CancellationToken>((h, _) => saved = h)
            .Returns(Task.CompletedTask);

        var svc = new ImportApplyService(repo.Object, new ImportRowMapper(), historyRepo.Object);
        var result = await svc.ApplyAsync(batch, new ImportApplyOptions());

        Assert.NotNull(result.HistoryId);
        Assert.NotNull(saved);
        Assert.Equal(result.HistoryId, saved!.Id);
        Assert.Equal(batch.Id, saved.BatchId);
        Assert.Equal(3, saved.Entries.Count);

        var skipEntry = saved.Entries.Single(e => e.RowIndex == 1);
        Assert.Equal(ImportBatchAction.Skipped, skipEntry.Action);
        Assert.Null(skipEntry.NewTradeId);
        Assert.Null(skipEntry.OverwrittenTradeJson);

        var overwriteEntry = saved.Entries.Single(e => e.RowIndex == 2);
        Assert.Equal(ImportBatchAction.Overwritten, overwriteEntry.Action);
        Assert.NotNull(overwriteEntry.NewTradeId);
        Assert.NotNull(overwriteEntry.OverwrittenTradeJson);
        Assert.Contains(existingId.ToString(), overwriteEntry.OverwrittenTradeJson);

        var addEntry = saved.Entries.Single(e => e.RowIndex == 3);
        Assert.Equal(ImportBatchAction.Added, addEntry.Action);
        Assert.NotNull(addEntry.NewTradeId);
        Assert.Null(addEntry.OverwrittenTradeJson);
    }

    [Fact]
    public async Task AutoCategorization_AssignsCategoryFromCounterparty()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var foodCat = Guid.NewGuid();
        var rules = new[]
        {
            new AutoCategorizationRule(
                Id: Guid.NewGuid(),
                KeywordPattern: "Starbucks",
                CategoryId: foodCat,
                MatchField: AutoCategorizationMatchField.Counterparty,
                AppliesTo: AutoCategorizationScope.Import),
        };
        var ruleRepo = new Mock<IAutoCategorizationRuleRepository>();
        ruleRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

        var batch = BankBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 27), -150m, "Starbucks Taipei", null));

        var svc = new ImportApplyService(repo.Object, new ImportRowMapper(), null, ruleRepo.Object);
        await svc.ApplyAsync(batch, new ImportApplyOptions());

        Assert.Single(added);
        Assert.Equal(foodCat, added[0].CategoryId);
    }

    [Fact]
    public async Task AutoCategorization_FiltersRulesByImportedTradeType()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var incomeCat = Guid.NewGuid();
        var expenseCat = Guid.NewGuid();
        var rules = new[]
        {
            new AutoCategorizationRule(
                Id: Guid.NewGuid(),
                KeywordPattern: "Uber",
                CategoryId: incomeCat,
                Priority: 0,
                IsEnabled: true,
                MatchCaseSensitive: false,
                MatchField: AutoCategorizationMatchField.Counterparty,
                AppliesTo: AutoCategorizationScope.Import),
            new AutoCategorizationRule(
                Id: Guid.NewGuid(),
                KeywordPattern: "Uber",
                CategoryId: expenseCat,
                Priority: 1,
                IsEnabled: true,
                MatchCaseSensitive: false,
                MatchField: AutoCategorizationMatchField.Counterparty,
                AppliesTo: AutoCategorizationScope.Import),
        };
        var ruleRepo = new Mock<IAutoCategorizationRuleRepository>();
        ruleRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);
        var categoryRepo = new Mock<ICategoryRepository>();
        categoryRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ExpenseCategory(incomeCat, "薪資", CategoryKind.Income),
                new ExpenseCategory(expenseCat, "交通", CategoryKind.Expense),
            ]);

        var batch = BankBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 27), -150m, "Uber Trip", null));

        var svc = new ImportApplyService(
            repo.Object,
            new ImportRowMapper(),
            null,
            ruleRepo.Object,
            categoryRepo.Object);
        await svc.ApplyAsync(batch, new ImportApplyOptions());

        Assert.Single(added);
        Assert.Equal(expenseCat, added[0].CategoryId);
    }

    [Fact]
    public async Task AutoCategorization_NoMatch_LeavesCategoryNull()
    {
        var added = new List<Trade>();
        var repo = NewRepo(added);
        var rules = new[]
        {
            new AutoCategorizationRule(
                Id: Guid.NewGuid(),
                KeywordPattern: "Starbucks",
                CategoryId: Guid.NewGuid(),
                AppliesTo: AutoCategorizationScope.Import),
        };
        var ruleRepo = new Mock<IAutoCategorizationRuleRepository>();
        ruleRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);

        var batch = BankBatch(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 27), -150m, "Mister Donut", null));

        var svc = new ImportApplyService(repo.Object, new ImportRowMapper(), null, ruleRepo.Object);
        await svc.ApplyAsync(batch, new ImportApplyOptions());

        Assert.Single(added);
        Assert.Null(added[0].CategoryId);
    }

    private static Mock<ITradeRepository> NewRepo(List<Trade> sink)
    {
        var mock = new Mock<ITradeRepository>();
        mock.Setup(r => r.AddAsync(It.IsAny<Trade>()))
            .Callback<Trade, CancellationToken>((t, _) => sink.Add(t))
            .Returns(Task.CompletedTask);
        mock.Setup(r => r.ApplyAtomicAsync(It.IsAny<IReadOnlyList<TradeMutation>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<TradeMutation>, CancellationToken>((mutations, _) =>
            {
                foreach (var mutation in mutations)
                {
                    if (mutation is AddTradeMutation add)
                        sink.Add(add.Trade);
                }
            })
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
