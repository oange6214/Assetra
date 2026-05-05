using Assetra.WPF.Features.Snackbar;
using Assetra.WPF.Infrastructure;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class SnackbarViewModelTests
{
    [Fact]
    public void Show_QueuesItemWithoutStartingAutoDismiss()
    {
        var vm = new SnackbarViewModel();

        vm.Show("Database migrated", SnackbarKind.Warning);

        Assert.Single(vm.Items);
        Assert.Equal("Database migrated", vm.Items[0].Text);
        Assert.Equal(SnackbarKind.Warning, vm.Items[0].Kind);
    }

    [Fact]
    public void Show_RemovesOldestItemWhenQueueIsFull()
    {
        var vm = new SnackbarViewModel();

        vm.Show("one", SnackbarKind.Info);
        vm.Show("two", SnackbarKind.Info);
        vm.Show("three", SnackbarKind.Info);
        vm.Show("four", SnackbarKind.Info);
        vm.Show("five", SnackbarKind.Info);
        vm.Show("six", SnackbarKind.Info);

        Assert.Equal(5, vm.Items.Count);
        Assert.DoesNotContain(vm.Items, item => item.Text == "one");
        Assert.Equal("two", vm.Items[0].Text);
        Assert.Equal("six", vm.Items[^1].Text);
    }
}
