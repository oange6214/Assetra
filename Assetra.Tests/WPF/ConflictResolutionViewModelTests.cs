using Moq;
using Assetra.Application.Sync;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
using Assetra.WPF.Features.Settings;
using Xunit;

namespace Assetra.Tests.WPF;

public class ConflictResolutionViewModelTests
{
    private static SyncConflict MakeConflict(Guid id)
    {
        var local = new SyncEnvelope(id, "Category", "{}", new EntityVersion(2, DateTimeOffset.UtcNow, "device-A"));
        var remote = new SyncEnvelope(id, "Category", "{}", new EntityVersion(2, DateTimeOffset.UtcNow, "device-B"));
        return new SyncConflict(local, remote);
    }

    [Fact]
    public async Task Reload_DrainsQueueIntoItems()
    {
        var queue = new CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await queue.RecordManualConflictAsync(new[] { MakeConflict(id1), MakeConflict(id2) });

        var vm = new ConflictResolutionViewModel(queue, queue);
        vm.ReloadCommand.Execute(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.True(vm.HasItems);
        Assert.Empty(queue.DrainManualConflicts()); // already drained
    }

    [Fact]
    public void Reload_StatusReflectsEmptyQueue()
    {
        var queue = new CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object);
        var vm = new ConflictResolutionViewModel(queue, queue);
        vm.ReloadCommand.Execute(null);

        Assert.Empty(vm.Items);
        Assert.False(vm.HasItems);
        Assert.Contains("No pending", vm.StatusMessage);
    }

    [Fact]
    public async Task KeepLocal_RemovesItemWithoutTouchingStore()
    {
        var store = new Mock<ICategorySyncStore>();
        var queue = new CategoryLocalChangeQueue(store.Object);
        await queue.RecordManualConflictAsync(new[] { MakeConflict(Guid.NewGuid()) });

        var vm = new ConflictResolutionViewModel(queue, queue);
        vm.ReloadCommand.Execute(null);
        var row = vm.Items[0];

        await vm.KeepLocalCommand.ExecuteAsync(row);

        Assert.Empty(vm.Items);
        store.Verify(s => s.ApplyRemoteAsync(It.IsAny<IReadOnlyList<SyncEnvelope>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UseRemote_AppliesRemoteEnvelopeAndRemovesItem()
    {
        var store = new Mock<ICategorySyncStore>();
        store.Setup(s => s.ApplyRemoteAsync(It.IsAny<IReadOnlyList<SyncEnvelope>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var queue = new CategoryLocalChangeQueue(store.Object);
        var conflict = MakeConflict(Guid.NewGuid());
        await queue.RecordManualConflictAsync(new[] { conflict });

        var vm = new ConflictResolutionViewModel(queue, queue);
        vm.ReloadCommand.Execute(null);
        var row = vm.Items[0];

        await vm.UseRemoteCommand.ExecuteAsync(row);

        Assert.Empty(vm.Items);
        store.Verify(s => s.ApplyRemoteAsync(
            It.Is<IReadOnlyList<SyncEnvelope>>(list => list.Count == 1 && list[0] == conflict.Remote),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
