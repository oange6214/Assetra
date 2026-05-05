using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Fire;
using Assetra.WPF.Features.Goals;
using CommunityToolkit.Mvvm.Messaging;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class GoalsViewModelTests
{
    [Fact]
    public async Task FireGoalSaved_BeforeInitialLoad_DoesNotMarkGoalsLoaded()
    {
        var storedGoal = new FinancialGoal(Guid.NewGuid(), "Emergency fund", 100_000m, 20_000m, null, null);
        var fireGoal = new FinancialGoal(Guid.NewGuid(), "FIRE", 15_000_000m, 1_000_000m, null, null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([storedGoal, fireGoal]);

        var vm = new GoalsViewModel(repo.Object);
        try
        {
            WeakReferenceMessenger.Default.Send(new FireGoalSavedMessage(fireGoal));

            Assert.False(vm.IsLoaded);
            Assert.Empty(vm.Goals);

            await vm.LoadAsync();

            Assert.True(vm.IsLoaded);
            Assert.Equal(new[] { "Emergency fund", "FIRE" }, vm.Goals.Select(g => g.Name).ToArray());
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public async Task AddAsync_RejectsInvalidCurrentAmount(string currentAmount)
    {
        var repo = new Mock<IFinancialGoalRepository>();
        var vm = new GoalsViewModel(repo.Object)
        {
            AddName = "Vacation",
            AddTargetAmount = "100000",
            AddCurrentAmount = currentAmount,
        };

        try
        {
            await vm.AddCommand.ExecuteAsync(null);

            Assert.Equal("Current amount must be 0 or greater", vm.AddError);
            Assert.Empty(vm.Goals);
            repo.Verify(r => r.AddAsync(It.IsAny<FinancialGoal>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }
}
