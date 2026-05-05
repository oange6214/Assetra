using System.Reflection;
using Moq;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import;
using Assetra.WPF.Features.Import;
using Assetra.WPF.Infrastructure;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class ImportViewModelTests
{
    [Fact]
    public async Task SelectedCashAccountChanged_IgnoresStaleConflictRefresh()
    {
        var firstAccountId = Guid.NewGuid();
        var latestAccountId = Guid.NewGuid();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latestGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detector = new Mock<IImportConflictDetector>();
        detector.Setup(d => d.DetectAsync(
                It.IsAny<ImportBatch>(),
                It.IsAny<ImportApplyOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<ImportBatch, ImportApplyOptions?, CancellationToken>(async (batch, options, _) =>
            {
                var accountId = options?.CashAccountId;
                if (accountId == firstAccountId)
                {
                    firstStarted.SetResult();
                    await firstGate.Task;
                }
                else if (accountId == latestAccountId)
                {
                    latestStarted.SetResult();
                    await latestGate.Task;
                }

                return batch with
                {
                    Conflicts =
                    [
                        new ImportConflict(batch.Rows[0], accountId, null),
                    ],
                };
            });
        var vm = new ImportViewModel(
            Mock.Of<IImportFormatDetector>(),
            new ImportParserFactory(),
            detector.Object,
            Mock.Of<IImportApplyService>(),
            Mock.Of<IImportBatchHistoryRepository>(),
            Mock.Of<IImportRollbackService>(),
            Mock.Of<IAssetRepository>(),
            Mock.Of<ISnackbarService>());
        var row = new ImportPreviewRow(1, new DateOnly(2026, 5, 1), -100m, "Store", "memo");
        var batch = new ImportBatch(
            Guid.NewGuid(),
            "test.csv",
            ImportFileType.Csv,
            ImportFormat.Generic,
            DateTimeOffset.UtcNow,
            [row],
            []);
        SetCurrentBatch(vm, batch);

        vm.SelectedCashAccount = new CashAccountOption(firstAccountId, "First");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        vm.SelectedCashAccount = new CashAccountOption(latestAccountId, "Latest");
        await latestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        latestGate.SetResult();
        await WaitForAsync(() => CurrentBatch(vm)?.Conflicts.SingleOrDefault()?.ExistingTradeId == latestAccountId);
        firstGate.SetResult();
        await Task.Delay(50);

        Assert.Equal(latestAccountId, CurrentBatch(vm)?.Conflicts.Single().ExistingTradeId);
    }

    private static ImportBatch? CurrentBatch(ImportViewModel vm) =>
        typeof(ImportViewModel)
            .GetField("_currentBatch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(vm) as ImportBatch;

    private static void SetCurrentBatch(ImportViewModel vm, ImportBatch batch) =>
        typeof(ImportViewModel)
            .GetField("_currentBatch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, batch);

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
}
