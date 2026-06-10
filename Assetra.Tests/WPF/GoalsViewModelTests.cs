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

    [Fact] // WHY: 儀表板會在背景報價執行緒上 fire-and-forget 呼叫 LoadAsync（GoalsWidget 預取）。
           // LoadAsync 必須能在非 UI 執行緒被呼叫而仍正確載入，不可拋出或留空集合。
    public async Task LoadAsync_InvokedFromBackgroundThread_PopulatesGoals()
    {
        var storedGoal = new FinancialGoal(Guid.NewGuid(), "Emergency fund", 100_000m, 20_000m, null, null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([storedGoal]);

        var vm = new GoalsViewModel(repo.Object);
        try
        {
            await Task.Run(() => vm.LoadAsync());

            Assert.True(vm.IsLoaded);
            Assert.Null(vm.ErrorMessage);
            Assert.NotEmpty(vm.Goals);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact] // WHY: UpsertGoal 透過 FireGoalSavedMessage messenger handler 變更綁定的 _goals 集合。
           // 訊息處理器會在呼叫 Send 的執行緒上執行，未來若有背景執行緒 sender，跨執行緒變更
           // CollectionView 會 throw。此測試保證從背景執行緒觸發 upsert 仍能完成並正確更新，不拋出。
           // 註：測試主機 Application.Current 為 null，故走 inline path——這是契約/守門測試，
           // 非真正的跨執行緒重現（與 LoadAsync 背景執行緒測試同樣的限制）。
    public async Task UpsertGoal_InvokedFromBackgroundThread_UpsertsWithoutThrowing()
    {
        var existingGoal = new FinancialGoal(Guid.NewGuid(), "Emergency", 200_000m, 50_000m, null, null);
        var fireGoal = new FinancialGoal(Guid.NewGuid(), "FIRE", 15_000_000m, 1_000_000m, null, null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingGoal]);

        var vm = new GoalsViewModel(repo.Object);
        try
        {
            await vm.LoadAsync();

            // Drive UpsertGoal exactly as production does: a FireGoalSavedMessage sent
            // from a background thread (the messenger handler runs UpsertGoal inline).
            await Task.Run(() =>
                WeakReferenceMessenger.Default.Send(new FireGoalSavedMessage(fireGoal)));

            Assert.True(vm.IsLoaded);
            Assert.Null(vm.ErrorMessage);
            Assert.Equal(2, vm.Goals.Count);
            var fireRow = vm.Goals.Single(g => g.Id == fireGoal.Id);
            Assert.Equal("FIRE", fireRow.Name);
            Assert.Equal(15_000_000m, fireRow.Goal.TargetAmount);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact] // WHY: 儀表板每個 IPortfolioPositionFeed 報價 tick 都 fire-and-forget 預取 GoalsWidget.LoadAsync。
           // LoadAsync 必須是一次性（IsLoaded gate）——否則每 tick 都重查 repo 並清空/重建綁定集合，純浪費。
           // 語言/幣別的列更新走 OnLanguageChanged/OnCurrencyChanged 就地刷新，不需要重載，故 gate 安全。
    public async Task LoadAsync_IsOneShot_SecondCallDoesNotRequery()
    {
        var storedGoal = new FinancialGoal(Guid.NewGuid(), "Emergency fund", 100_000m, 20_000m, null, null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([storedGoal]);

        var vm = new GoalsViewModel(repo.Object);
        try
        {
            await vm.LoadAsync();
            var countAfterFirst = vm.Goals.Count;

            await vm.LoadAsync();

            repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(countAfterFirst, vm.Goals.Count);
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

    [Fact]
    public async Task AddAsync_PersistsAllGoalFields()
    {
        FinancialGoal? addedGoal = null;
        var deadline = new DateTime(2027, 6, 30);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<FinancialGoal>(), It.IsAny<CancellationToken>()))
            .Callback<FinancialGoal, CancellationToken>((goal, _) => addedGoal = goal)
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object)
        {
            AddName = "House fund",
            AddTargetAmount = "1,500,000",
            AddCurrentAmount = "250,000",
            AddDeadline = deadline,
            AddNotes = "Down payment target",
            AddLinkedAssetClass = "Investments",
        };

        try
        {
            await vm.AddCommand.ExecuteAsync(null);

            Assert.NotNull(addedGoal);
            Assert.Equal("House fund", addedGoal.Name);
            Assert.Equal(1_500_000m, addedGoal.TargetAmount);
            Assert.Equal(250_000m, addedGoal.CurrentAmount);
            Assert.Equal(new DateOnly(2027, 6, 30), addedGoal.Deadline);
            Assert.Equal("Down payment target", addedGoal.Notes);
            Assert.Equal("Investments", addedGoal.LinkedAssetClass);
            Assert.Null(addedGoal.PortfolioGroupId);
            Assert.Single(vm.Goals);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task EditAsync_PersistsAllGoalFieldsAndPreservesPortfolioGroup()
    {
        var groupId = Guid.NewGuid();
        var storedGoal = new FinancialGoal(
            Guid.NewGuid(),
            "Old goal",
            1_000_000m,
            100_000m,
            new DateOnly(2026, 12, 31),
            "Old note",
            LinkedAssetClass: "Investments",
            PortfolioGroupId: groupId);
        FinancialGoal? updatedGoal = null;
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([storedGoal]);
        repo.Setup(r => r.UpdateAsync(It.IsAny<FinancialGoal>(), It.IsAny<CancellationToken>()))
            .Callback<FinancialGoal, CancellationToken>((goal, _) => updatedGoal = goal)
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.LoadAsync();

            vm.EditCommand.Execute(vm.Goals[0]);
            vm.AddName = "Updated goal";
            vm.AddTargetAmount = "2,000,000";
            vm.AddCurrentAmount = "350,000";
            vm.AddDeadline = new DateTime(2028, 1, 31);
            vm.AddNotes = "Updated note";
            await vm.AddCommand.ExecuteAsync(null);

            Assert.NotNull(updatedGoal);
            Assert.Equal(storedGoal.Id, updatedGoal.Id);
            Assert.Equal("Updated goal", updatedGoal.Name);
            Assert.Equal(2_000_000m, updatedGoal.TargetAmount);
            Assert.Equal(350_000m, updatedGoal.CurrentAmount);
            Assert.Equal(new DateOnly(2028, 1, 31), updatedGoal.Deadline);
            Assert.Equal("Updated note", updatedGoal.Notes);
            Assert.Equal("Investments", updatedGoal.LinkedAssetClass);
            Assert.Equal(groupId, updatedGoal.PortfolioGroupId);
            Assert.Single(vm.Goals);
            Assert.Equal("Updated goal", vm.Goals[0].Name);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task EditAsync_PreservesPortfolioGroup_WhenCatalogIsUnavailable()
    {
        var groupId = Guid.NewGuid();
        var storedGoal = new FinancialGoal(
            Guid.NewGuid(),
            "Retirement bucket",
            1_000_000m,
            250_000m,
            new DateOnly(2026, 12, 31),
            "Keep this linked",
            LinkedAssetClass: null,
            PortfolioGroupId: groupId);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([storedGoal]);

        var vm = new GoalsViewModel(repo.Object);
        try
        {
            await vm.LoadAsync();

            vm.EditCommand.Execute(vm.Goals[0]);
            await vm.AddCommand.ExecuteAsync(null);

            repo.Verify(
                r => r.UpdateAsync(
                    It.Is<FinancialGoal>(goal => goal.PortfolioGroupId == groupId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public void OpenGoalDetailCommand_SelectsGoalAndCloseClears()
    {
        var repo = new Mock<IFinancialGoalRepository>();
        var goal = new FinancialGoal(Guid.NewGuid(), "Vacation", 100_000m, 10_000m, null, null);
        var row = new GoalRowViewModel(goal);
        var vm = new GoalsViewModel(repo.Object);

        try
        {
            Assert.False(vm.IsGoalDetailOpen);

            vm.OpenGoalDetailCommand.Execute(row);

            Assert.Same(row, vm.SelectedGoal);
            Assert.True(vm.IsGoalDetailOpen);

            vm.CloseGoalDetailCommand.Execute(null);

            Assert.Null(vm.SelectedGoal);
            Assert.False(vm.IsGoalDetailOpen);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailCommand_ShowsFireSourceDetails_WhenGoalComesFromFire()
    {
        var repo = new Mock<IFinancialGoalRepository>();
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "FIRE",
            15_000_000m,
            8_506_900m,
            null,
            "Generated from FIRE scenario \"Base\" (scenario-id).");
        var row = new GoalRowViewModel(goal);
        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.OpenGoalDetailCommand.ExecuteAsync(row);

            Assert.True(vm.SelectedDetailHasFireSource);
            Assert.Equal("NT$15,000,000", vm.SelectedDetailFireRequiredAssetsDisplay);
            Assert.Equal("Base", vm.SelectedDetailFireScenarioDisplay);
            Assert.Equal("Not recorded", vm.SelectedDetailFireLastSyncDisplay);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task RemoveAsync_ClearsSelectedGoal_WhenRemovingSelectedGoal()
    {
        var storedGoal = new FinancialGoal(Guid.NewGuid(), "Vacation", 100_000m, 10_000m, null, null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([storedGoal]);
        repo.Setup(r => r.RemoveAsync(storedGoal.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.LoadAsync();
            vm.OpenGoalDetailCommand.Execute(vm.Goals[0]);

            vm.RemoveCommand.Execute(vm.Goals[0]);
            await vm.ConfirmDialogYesCommand.ExecuteAsync(null);

            Assert.Null(vm.SelectedGoal);
            Assert.False(vm.IsGoalDetailOpen);
            Assert.Empty(vm.Goals);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlySelectedGoalAndRefreshesSummary()
    {
        var removedGoal = new FinancialGoal(Guid.NewGuid(), "Vacation", 100_000m, 10_000m, null, null);
        var remainingGoal = new FinancialGoal(Guid.NewGuid(), "Emergency", 200_000m, 50_000m, null, null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([removedGoal, remainingGoal]);
        repo.Setup(r => r.RemoveAsync(removedGoal.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.LoadAsync();

            vm.RemoveCommand.Execute(vm.Goals[0]);
            await vm.ConfirmDialogYesCommand.ExecuteAsync(null);

            Assert.Single(vm.Goals);
            Assert.Equal("Emergency", vm.Goals[0].Name);
            Assert.Equal(1, vm.GoalCount);
            Assert.Equal(200_000m, vm.TotalTarget);
            Assert.Equal(50_000m, vm.TotalCurrent);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task FireGoalSaved_AfterInitialLoad_UpdatesGoalWithoutDroppingOtherGoals()
    {
        var fireGoal = new FinancialGoal(Guid.NewGuid(), "FIRE", 15_000_000m, 1_000_000m, null, null);
        var otherGoal = new FinancialGoal(Guid.NewGuid(), "Emergency", 200_000m, 50_000m, null, null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([fireGoal, otherGoal]);

        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.LoadAsync();

            var updatedFireGoal = fireGoal with
            {
                TargetAmount = 20_000_000m,
                CurrentAmount = 8_000_000m,
            };
            WeakReferenceMessenger.Default.Send(new FireGoalSavedMessage(updatedFireGoal));

            Assert.Equal(2, vm.Goals.Count);
            Assert.Equal("Emergency", vm.Goals.Single(goal => goal.Id == otherGoal.Id).Name);
            var fireRow = vm.Goals.Single(goal => goal.Id == fireGoal.Id);
            Assert.Equal(20_000_000m, fireRow.Goal.TargetAmount);
            Assert.Equal(8_000_000m, fireRow.Goal.CurrentAmount);
            Assert.Equal(20_200_000m, vm.TotalTarget);
            Assert.Equal(8_050_000m, vm.TotalCurrent);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailAsync_LoadsMilestonesAndFundingRules()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            1_000_000m,
            250_000m,
            new DateOnly(2027, 12, 31),
            null);
        var milestone = new GoalMilestone(
            Guid.NewGuid(),
            goal.Id,
            new DateOnly(2026, 12, 31),
            500_000m,
            "Halfway",
            IsAchieved: false);
        var fundingRule = new GoalFundingRule(
            Guid.NewGuid(),
            goal.Id,
            20_000m,
            RecurrenceFrequency.Monthly,
            SourceCashAccountId: null,
            new DateOnly(2026, 1, 1),
            EndDate: null);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var milestones = new Mock<IGoalMilestoneRepository>();
        milestones.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([milestone]);
        var fundingRules = new Mock<IGoalFundingRuleRepository>();
        fundingRules.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundingRule]);

        var vm = new GoalsViewModel(
            repo.Object,
            milestoneRepository: milestones.Object,
            fundingRuleRepository: fundingRules.Object);

        try
        {
            await vm.LoadAsync();

            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Single(vm.SelectedMilestones);
            Assert.Equal("Halfway", vm.SelectedMilestones[0].Label);
            Assert.Single(vm.SelectedFundingRules);
            Assert.Equal("NT$20,000", vm.SelectedFundingRules[0].AmountDisplay);
            Assert.False(vm.IsGoalDetailLoading);
            Assert.Null(vm.GoalDetailError);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public void GoalFundingRuleRow_DisplaysSourceAccountAndNextContribution()
    {
        var sourceAccountId = Guid.NewGuid();
        var rule = new GoalFundingRule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            12_000m,
            RecurrenceFrequency.Monthly,
            sourceAccountId,
            new DateOnly(2026, 5, 10),
            EndDate: null);

        var row = new GoalFundingRuleRowViewModel(
            rule,
            sourceAccountNameResolver: id => id == sourceAccountId ? "Main savings" : null,
            today: new DateOnly(2026, 6, 3));

        Assert.Equal("Main savings", row.SourceAccountDisplay);
        Assert.Equal("2026-06-10 - NT$12,000", row.NextContributionDisplay);
    }

    [Fact]
    public async Task OpenGoalDetailAsync_DerivesMilestoneCompletionFromCurrentProgress()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            1_000_000m,
            600_000m,
            new DateOnly(2027, 12, 31),
            null);
        var milestone = new GoalMilestone(
            Guid.NewGuid(),
            goal.Id,
            new DateOnly(2026, 12, 31),
            500_000m,
            "Halfway",
            IsAchieved: false);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var milestones = new Mock<IGoalMilestoneRepository>();
        milestones.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([milestone]);

        var vm = new GoalsViewModel(
            repo.Object,
            milestoneRepository: milestones.Object);

        try
        {
            await vm.LoadAsync();

            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Single(vm.SelectedMilestones);
            Assert.True(vm.SelectedMilestones[0].IsAchieved);
            Assert.Equal("Achieved", vm.SelectedMilestones[0].StatusDisplay);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailAsync_ComputesPlanningHelperFromFundingRules()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            120_000m,
            0m,
            today.AddMonths(11),
            null);
        var fundingRule = new GoalFundingRule(
            Guid.NewGuid(),
            goal.Id,
            8_000m,
            RecurrenceFrequency.Monthly,
            SourceCashAccountId: null,
            today,
            EndDate: null);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var fundingRules = new Mock<IGoalFundingRuleRepository>();
        fundingRules.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundingRule]);

        var vm = new GoalsViewModel(repo.Object, fundingRuleRepository: fundingRules.Object);

        try
        {
            await vm.LoadAsync();

            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Equal("NT$10,000", vm.SelectedRequiredMonthlyContributionDisplay);
            Assert.Equal("NT$8,000", vm.SelectedMonthlyFundingDisplay);
            Assert.Equal("NT$2,000", vm.SelectedMonthlyFundingGapDisplay);
            Assert.Equal("15 months", vm.SelectedProjectedCompletionDisplay);
            Assert.Null(vm.GoalPlanningWarning);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailAsync_UsesResolvedProgressAmount_ForAutoTrackedGoalPlanning()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "Net worth goal",
            120_000m,
            0m,
            today.AddMonths(11),
            null,
            LinkedAssetClass: "NetWorth");

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var progressAmounts = new Mock<IGoalProgressAmountProvider>();
        progressAmounts.Setup(p => p.GetCurrentAmountAsync(goal, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60_000m);

        var vm = new GoalsViewModel(repo.Object, progressAmountProvider: progressAmounts.Object);

        try
        {
            await vm.LoadAsync();

            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Equal("NT$60,000", vm.SelectedDetailCurrentDisplay);
            Assert.Equal("NT$60,000", vm.SelectedDetailRemainingDisplay);
            Assert.Equal("50.0%", vm.SelectedDetailProgressDisplay);
            Assert.Equal("NT$5,000", vm.SelectedRequiredMonthlyContributionDisplay);
            Assert.Equal("NT$5,000", vm.SelectedMonthlyFundingGapDisplay);
            progressAmounts.Verify(
                p => p.GetCurrentAmountAsync(goal, It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailAsync_NextActionRecommendsFundingRule_WhenNoFundingRules()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            120_000m,
            0m,
            today.AddMonths(11),
            null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var fundingRules = new Mock<IGoalFundingRuleRepository>();
        fundingRules.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var vm = new GoalsViewModel(repo.Object, fundingRuleRepository: fundingRules.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Equal("Create a funding rule of NT$10,000/month.", vm.SelectedDetailNextActionDisplay);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailAsync_PlanningWarns_WhenGoalIsAlreadyCompleted()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "Done",
            100_000m,
            120_000m,
            today.AddMonths(6),
            null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);

        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Equal("NT$0", vm.SelectedRequiredMonthlyContributionDisplay);
            Assert.Equal("NT$0", vm.SelectedMonthlyFundingGapDisplay);
            Assert.Equal("This goal is already reached.", vm.GoalPlanningWarning);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailAsync_PlanningWarns_WhenGoalHasNoDeadline()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "No deadline",
            100_000m,
            20_000m,
            null,
            null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);

        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Equal("—", vm.SelectedRequiredMonthlyContributionDisplay);
            Assert.Equal("Set a deadline to calculate the required monthly contribution.", vm.GoalPlanningWarning);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailAsync_PlanningWarns_WhenDeadlineHasPassed()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "Past deadline",
            100_000m,
            20_000m,
            today.AddDays(-1),
            null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);

        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Equal("—", vm.SelectedRequiredMonthlyContributionDisplay);
            Assert.Equal("The deadline has passed before this goal was reached.", vm.GoalPlanningWarning);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task OpenGoalDetailAsync_PlanningWarns_WhenTargetIsZero()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "Invalid target",
            0m,
            0m,
            today.AddMonths(6),
            null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);

        var vm = new GoalsViewModel(repo.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.Equal("—", vm.SelectedRequiredMonthlyContributionDisplay);
            Assert.Equal("Target amount is not set.", vm.GoalPlanningWarning);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task AddSelectedMilestoneAsync_CreatesMilestoneForSelectedGoalAndRefreshesList()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            1_000_000m,
            250_000m,
            new DateOnly(2027, 12, 31),
            null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var milestones = new Mock<IGoalMilestoneRepository>();
        milestones.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        GoalMilestone? addedMilestone = null;
        milestones.Setup(r => r.AddAsync(It.IsAny<GoalMilestone>(), It.IsAny<CancellationToken>()))
            .Callback<GoalMilestone, CancellationToken>((milestone, _) => addedMilestone = milestone)
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object, milestoneRepository: milestones.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);
            vm.SelectedMilestoneLabel = "Down payment";
            vm.SelectedMilestoneTargetAmount = "500000";
            vm.SelectedMilestoneTargetDate = new DateTime(2026, 12, 31);

            await vm.AddSelectedMilestoneCommand.ExecuteAsync(null);

            Assert.NotNull(addedMilestone);
            Assert.Equal(goal.Id, addedMilestone.GoalId);
            Assert.Equal("Down payment", addedMilestone.Label);
            Assert.Equal(500_000m, addedMilestone.TargetAmount);
            Assert.Equal(new DateOnly(2026, 12, 31), addedMilestone.TargetDate);
            Assert.Single(vm.SelectedMilestones);
            Assert.Equal("Down payment", vm.SelectedMilestones[0].Label);
            Assert.Equal(string.Empty, vm.SelectedMilestoneLabel);
            Assert.Equal(string.Empty, vm.SelectedMilestoneTargetAmount);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task AddSelectedFundingRuleAsync_CreatesRuleForSelectedGoalAndUpdatesPlanning()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            120_000m,
            0m,
            today.AddMonths(11),
            null);
        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var fundingRules = new Mock<IGoalFundingRuleRepository>();
        fundingRules.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        GoalFundingRule? addedRule = null;
        fundingRules.Setup(r => r.AddAsync(It.IsAny<GoalFundingRule>(), It.IsAny<CancellationToken>()))
            .Callback<GoalFundingRule, CancellationToken>((rule, _) => addedRule = rule)
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object, fundingRuleRepository: fundingRules.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);
            vm.SelectedFundingAmount = "4000";
            vm.SelectedFundingFrequency = RecurrenceFrequency.Monthly;
            vm.SelectedFundingStartDate = DateTime.Today;

            Assert.Equal("NT$10,000", vm.SelectedRequiredMonthlyContributionDisplay);
            Assert.Equal("NT$0", vm.SelectedMonthlyFundingDisplay);

            await vm.AddSelectedFundingRuleCommand.ExecuteAsync(null);

            Assert.NotNull(addedRule);
            Assert.Equal(goal.Id, addedRule.GoalId);
            Assert.Equal(4_000m, addedRule.Amount);
            Assert.Equal(RecurrenceFrequency.Monthly, addedRule.Frequency);
            Assert.Single(vm.SelectedFundingRules);
            Assert.Equal("NT$4,000", vm.SelectedMonthlyFundingDisplay);
            Assert.Equal("NT$6,000", vm.SelectedMonthlyFundingGapDisplay);
            Assert.Equal(string.Empty, vm.SelectedFundingAmount);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task EditSelectedMilestoneAsync_UpdatesExistingMilestoneAndRefreshesRow()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            1_000_000m,
            250_000m,
            new DateOnly(2027, 12, 31),
            null);
        var milestone = new GoalMilestone(
            Guid.NewGuid(),
            goal.Id,
            new DateOnly(2026, 6, 30),
            400_000m,
            "First target",
            IsAchieved: false);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var milestones = new Mock<IGoalMilestoneRepository>();
        milestones.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([milestone]);
        GoalMilestone? updatedMilestone = null;
        milestones.Setup(r => r.UpdateAsync(It.IsAny<GoalMilestone>(), It.IsAny<CancellationToken>()))
            .Callback<GoalMilestone, CancellationToken>((value, _) => updatedMilestone = value)
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object, milestoneRepository: milestones.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            vm.EditSelectedMilestoneCommand.Execute(vm.SelectedMilestones[0]);
            vm.SelectedMilestoneLabel = "Closing target";
            vm.SelectedMilestoneTargetAmount = "750000";
            vm.SelectedMilestoneTargetDate = new DateTime(2026, 12, 31);

            await vm.AddSelectedMilestoneCommand.ExecuteAsync(null);

            Assert.NotNull(updatedMilestone);
            Assert.Equal(milestone.Id, updatedMilestone.Id);
            Assert.Equal(goal.Id, updatedMilestone.GoalId);
            Assert.Equal("Closing target", updatedMilestone.Label);
            Assert.Equal(750_000m, updatedMilestone.TargetAmount);
            Assert.Equal(new DateOnly(2026, 12, 31), updatedMilestone.TargetDate);
            Assert.Single(vm.SelectedMilestones);
            Assert.Equal("Closing target", vm.SelectedMilestones[0].Label);
            Assert.Equal(string.Empty, vm.SelectedMilestoneLabel);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task RemoveSelectedMilestoneAsync_RemovesExistingMilestoneAndRefreshesRows()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            1_000_000m,
            250_000m,
            new DateOnly(2027, 12, 31),
            null);
        var milestone = new GoalMilestone(
            Guid.NewGuid(),
            goal.Id,
            new DateOnly(2026, 6, 30),
            400_000m,
            "First target",
            IsAchieved: false);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var milestones = new Mock<IGoalMilestoneRepository>();
        milestones.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([milestone]);
        Guid? removedId = null;
        milestones.Setup(r => r.RemoveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => removedId = id)
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object, milestoneRepository: milestones.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            await vm.RemoveSelectedMilestoneCommand.ExecuteAsync(vm.SelectedMilestones[0]);

            Assert.Equal(milestone.Id, removedId);
            Assert.Empty(vm.SelectedMilestones);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task EditSelectedFundingRuleAsync_UpdatesExistingRuleAndRecomputesPlanning()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            120_000m,
            0m,
            today.AddMonths(11),
            null);
        var fundingRule = new GoalFundingRule(
            Guid.NewGuid(),
            goal.Id,
            4_000m,
            RecurrenceFrequency.Monthly,
            SourceCashAccountId: Guid.NewGuid(),
            today,
            EndDate: null);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var fundingRules = new Mock<IGoalFundingRuleRepository>();
        fundingRules.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundingRule]);
        GoalFundingRule? updatedRule = null;
        fundingRules.Setup(r => r.UpdateAsync(It.IsAny<GoalFundingRule>(), It.IsAny<CancellationToken>()))
            .Callback<GoalFundingRule, CancellationToken>((value, _) => updatedRule = value)
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object, fundingRuleRepository: fundingRules.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            vm.EditSelectedFundingRuleCommand.Execute(vm.SelectedFundingRules[0]);
            vm.SelectedFundingAmount = "8000";
            vm.SelectedFundingFrequency = RecurrenceFrequency.Monthly;
            vm.SelectedFundingStartDate = DateTime.Today;

            await vm.AddSelectedFundingRuleCommand.ExecuteAsync(null);

            Assert.NotNull(updatedRule);
            Assert.Equal(fundingRule.Id, updatedRule.Id);
            Assert.Equal(goal.Id, updatedRule.GoalId);
            Assert.Equal(fundingRule.SourceCashAccountId, updatedRule.SourceCashAccountId);
            Assert.Equal(8_000m, updatedRule.Amount);
            Assert.Equal(RecurrenceFrequency.Monthly, updatedRule.Frequency);
            Assert.Single(vm.SelectedFundingRules);
            Assert.Equal("NT$8,000", vm.SelectedMonthlyFundingDisplay);
            Assert.Equal("NT$2,000", vm.SelectedMonthlyFundingGapDisplay);
            Assert.Equal(string.Empty, vm.SelectedFundingAmount);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact]
    public async Task RemoveSelectedFundingRuleAsync_RemovesExistingRuleAndRecomputesPlanning()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            120_000m,
            0m,
            today.AddMonths(11),
            null);
        var fundingRule = new GoalFundingRule(
            Guid.NewGuid(),
            goal.Id,
            4_000m,
            RecurrenceFrequency.Monthly,
            SourceCashAccountId: null,
            today,
            EndDate: null);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var fundingRules = new Mock<IGoalFundingRuleRepository>();
        fundingRules.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundingRule]);
        Guid? removedId = null;
        fundingRules.Setup(r => r.RemoveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => removedId = id)
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object, fundingRuleRepository: fundingRules.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            await vm.RemoveSelectedFundingRuleCommand.ExecuteAsync(vm.SelectedFundingRules[0]);

            Assert.Equal(fundingRule.Id, removedId);
            Assert.Empty(vm.SelectedFundingRules);
            Assert.Equal("NT$0", vm.SelectedMonthlyFundingDisplay);
            Assert.Equal("NT$10,000", vm.SelectedMonthlyFundingGapDisplay);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact] // WHY: Q07 結果呈現精簡——清單預設可見，編輯表單收合。按「編輯」要自動展開
            // 表單（讓使用者看到填好的欄位），成功儲存後收合回清單，避免表單長期佔據版面。
    public async Task EditSelectedMilestone_ExpandsEditor_AndSuccessfulSubmitCollapsesIt()
    {
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            1_000_000m,
            250_000m,
            new DateOnly(2027, 12, 31),
            null);
        var milestone = new GoalMilestone(
            Guid.NewGuid(),
            goal.Id,
            new DateOnly(2026, 6, 30),
            400_000m,
            "First target",
            IsAchieved: false);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var milestones = new Mock<IGoalMilestoneRepository>();
        milestones.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([milestone]);
        milestones.Setup(r => r.UpdateAsync(It.IsAny<GoalMilestone>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object, milestoneRepository: milestones.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.False(vm.IsMilestoneEditorExpanded);

            vm.EditSelectedMilestoneCommand.Execute(vm.SelectedMilestones[0]);

            Assert.True(vm.IsMilestoneEditorExpanded);

            vm.SelectedMilestoneLabel = "Closing target";
            vm.SelectedMilestoneTargetAmount = "750000";
            vm.SelectedMilestoneTargetDate = new DateTime(2026, 12, 31);

            await vm.AddSelectedMilestoneCommand.ExecuteAsync(null);

            Assert.False(vm.IsMilestoneEditorExpanded);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact] // WHY: 同里程碑——定期投入編輯表單也要按「編輯」展開、成功儲存後收合，
            // 維持「清單為主、編輯為輔」的精簡版面。
    public async Task EditSelectedFundingRule_ExpandsEditor_AndSuccessfulSubmitCollapsesIt()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var goal = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            120_000m,
            0m,
            today.AddMonths(11),
            null);
        var fundingRule = new GoalFundingRule(
            Guid.NewGuid(),
            goal.Id,
            4_000m,
            RecurrenceFrequency.Monthly,
            SourceCashAccountId: null,
            today,
            EndDate: null);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goal]);
        var fundingRules = new Mock<IGoalFundingRuleRepository>();
        fundingRules.Setup(r => r.GetByGoalAsync(goal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundingRule]);
        fundingRules.Setup(r => r.UpdateAsync(It.IsAny<GoalFundingRule>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = new GoalsViewModel(repo.Object, fundingRuleRepository: fundingRules.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            Assert.False(vm.IsFundingEditorExpanded);

            vm.EditSelectedFundingRuleCommand.Execute(vm.SelectedFundingRules[0]);

            Assert.True(vm.IsFundingEditorExpanded);

            vm.SelectedFundingAmount = "8000";
            vm.SelectedFundingFrequency = RecurrenceFrequency.Monthly;
            vm.SelectedFundingStartDate = DateTime.Today;

            await vm.AddSelectedFundingRuleCommand.ExecuteAsync(null);

            Assert.False(vm.IsFundingEditorExpanded);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }

    [Fact] // WHY: 切換到另一個目標時，前一個目標展開的編輯表單必須收合，
            // 否則新目標一開啟就帶著殘留的展開狀態（與舊資料），版面與狀態都不一致。
    public async Task OpenGoalDetail_OnDifferentGoal_ResetsBothEditorsCollapsed()
    {
        var goalA = new FinancialGoal(
            Guid.NewGuid(),
            "House fund",
            1_000_000m,
            250_000m,
            new DateOnly(2027, 12, 31),
            null);
        var goalB = new FinancialGoal(
            Guid.NewGuid(),
            "Emergency fund",
            300_000m,
            50_000m,
            new DateOnly(2027, 6, 30),
            null);
        var milestoneA = new GoalMilestone(
            Guid.NewGuid(),
            goalA.Id,
            new DateOnly(2026, 6, 30),
            400_000m,
            "First target",
            IsAchieved: false);
        var fundingRuleA = new GoalFundingRule(
            Guid.NewGuid(),
            goalA.Id,
            4_000m,
            RecurrenceFrequency.Monthly,
            SourceCashAccountId: null,
            new DateOnly(2026, 1, 1),
            EndDate: null);

        var repo = new Mock<IFinancialGoalRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([goalA, goalB]);
        var milestones = new Mock<IGoalMilestoneRepository>();
        milestones.Setup(r => r.GetByGoalAsync(goalA.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([milestoneA]);
        milestones.Setup(r => r.GetByGoalAsync(goalB.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var fundingRules = new Mock<IGoalFundingRuleRepository>();
        fundingRules.Setup(r => r.GetByGoalAsync(goalA.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([fundingRuleA]);
        fundingRules.Setup(r => r.GetByGoalAsync(goalB.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var vm = new GoalsViewModel(
            repo.Object,
            milestoneRepository: milestones.Object,
            fundingRuleRepository: fundingRules.Object);

        try
        {
            await vm.LoadAsync();
            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[0]);

            vm.EditSelectedMilestoneCommand.Execute(vm.SelectedMilestones[0]);
            vm.EditSelectedFundingRuleCommand.Execute(vm.SelectedFundingRules[0]);
            Assert.True(vm.IsMilestoneEditorExpanded);
            Assert.True(vm.IsFundingEditorExpanded);

            await vm.OpenGoalDetailCommand.ExecuteAsync(vm.Goals[1]);

            Assert.False(vm.IsMilestoneEditorExpanded);
            Assert.False(vm.IsFundingEditorExpanded);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
    }
}
