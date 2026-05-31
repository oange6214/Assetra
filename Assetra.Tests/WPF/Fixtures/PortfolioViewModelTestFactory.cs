using System.Reactive.Concurrency;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Infrastructure;
using Moq;

namespace Assetra.Tests.WPF.Fixtures;

/// <summary>
/// H3 Phase 3 — canonical test entry-point for building <see cref="PortfolioViewModel"/>.
/// <para>
/// The legacy private <c>CreateVm()</c> helper inside <c>PortfolioViewModelTests</c>
/// remains for backward compatibility (72 callers). New tests — and any future
/// cross-class scenarios that need a real PortfolioViewModel — should use this
/// factory instead. It exposes the per-dependency mocks for fluent customisation:
/// </para>
/// <code>
/// var fx = new PortfolioViewModelTestFactory();
/// fx.WithEntries(PortfolioVmFixtures.MakeEntry("2330"));
/// fx.PortfolioRepo.Setup(...);
/// var vm = fx.Build();
/// </code>
/// </summary>
internal sealed class PortfolioViewModelTestFactory
{
    public Mock<IPortfolioRepository> PortfolioRepo { get; } = new();
    public Mock<IStockSearchService> Search { get; } = new();
    public FakeTradeRepo Trades { get; } = new();
    public IPositionQueryService? PositionQuery { get; set; }

    private List<PortfolioEntry> _mutableEntries = [PortfolioVmFixtures.MakeEntry()];

    public PortfolioViewModelTestFactory()
    {
        PortfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(() => _mutableEntries.ToList());
        PortfolioRepo.Setup(r => r.AddAsync(It.IsAny<PortfolioEntry>())).Returns(Task.CompletedTask);
        PortfolioRepo.Setup(r => r.UpdateAsync(It.IsAny<PortfolioEntry>())).Returns(Task.CompletedTask);
        PortfolioRepo.Setup(r => r.RemoveAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        PortfolioRepo.Setup(r => r.ArchiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) =>
            {
                var idx = _mutableEntries.FindIndex(e => e.Id == id);
                if (idx >= 0)
                    _mutableEntries[idx] = _mutableEntries[idx] with { IsActive = false };
            })
            .Returns(Task.CompletedTask);
    }

    public PortfolioViewModelTestFactory WithEntries(params PortfolioEntry[] entries)
    {
        _mutableEntries = entries.ToList();
        return this;
    }

    public PortfolioViewModel Build()
    {
        var (snapshotSvc, snapshotRepo) = PortfolioVmFixtures.SnapshotStubs();
        var (logRepo, backfill) = PortfolioVmFixtures.BackfillStubs(snapshotRepo);

        return new PortfolioViewModel(
            new PortfolioRepositories(PortfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: Trades),
            new PortfolioServices(
                PortfolioVmFixtures.SilentStockService().Object,
                Search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                PositionQuery: PositionQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
    }
}
