# Investment Groups V1 Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make investment groups a first-class workflow on the investment assets page without introducing a second grouping model.

**Architecture:** Use the existing `PortfolioGroup`, `PortfolioEntry.PortfolioGroupId`, and `Trade.PortfolioGroupId` model. Position rows must read their primary group from `PortfolioEntry.PortfolioGroupId`; trade-level group data remains historical context and must not overwrite the visible asset assignment. The first slice adds group-aware row projection, grouped/market/type view modes, and asset-context transaction preselection.

**Tech Stack:** .NET / WPF, CommunityToolkit.Mvvm source generators, `ICollectionView`, xUnit, Moq.

---

## Scope

This slice intentionally does not build full historical performance reports yet. It prepares the model and investment page UX, and includes a V1 current-membership group performance trend for the group detail panel.

## Implementation Checkpoint

**2026-05-25:** V1 slice is implemented and verified for the covered scope:

- Position rows now derive visible investment group assignment from `PortfolioEntry.PortfolioGroupId`.
- Aggregated rows with mixed active entry groups are marked as group conflicts.
- Investment position view modes support flat, group, market, and type grouping.
- Group headers now show visible-row subtotals: count, market value, cost, and PnL.
- First load defaults to `By group` only when at least one user-created group exists; system-only catalogs stay flat.
- The selected asset side panel shows the current investment group chip, with warning styling for mixed-group rows.
- Asset-context transaction entry preserves the selected asset group when opening the dialog.
- Editing a selected investment asset can now change its investment group assignment for the currently visible lots.
- Position group updates are persisted through `PortfolioEntry.PortfolioGroupId` and included in SQLite sync payloads.
- The selected asset group chip now opens a group detail side panel with current holdings, market value, cost, estimated P&L, and today change.
- The group detail side panel now includes current-member investment transactions and refreshes when trades reload.
- The group detail side panel now includes a current-membership performance trend, aggregated from available holding sparklines and compared with current paid cost.
- Allocation overview no longer depends on a treemap-only reading path; the active layout uses row-based allocation bars plus a sortable grid, and tiny holdings display as `<0.1%` instead of rounding to `0.0%`.
- Editing an aggregated position with mixed group assignments now shows an explicit warning and saving applies the chosen group to all visible lots.

Verified with:

```powershell
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter "FullyQualifiedName~PortfolioViewModelTests.LoadAsync_UsesPortfolioEntryGroupForPositionRow|FullyQualifiedName~PortfolioViewModelTests.LoadAsync_MarksAggregatedPositionWhenEntryGroupsConflict|FullyQualifiedName~PortfolioViewModelTests.SetPositionViewMode_GroupsPositionsViewBySelectedDimension|FullyQualifiedName~PortfolioViewModelTests.SetPositionViewMode_Group_UpdatesVisibleGroupSummaries|FullyQualifiedName~PortfolioViewModelTests.LoadAsync_DefaultsToGroupViewWhenCustomGroupsExist|FullyQualifiedName~PortfolioViewModelTests.LoadAsync_KeepsFlatViewWhenOnlySystemGroupExists|FullyQualifiedName~TransactionDialogViewModelTests.OpenTxDialogForPosition_UsesPositionGroupWhenCatalogIsLoaded" --no-restore
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter "FullyQualifiedName~PositionMetadataWorkflowServiceTests.UpdateGroupAsync_UpdatesEveryEntryWithoutChangingOtherMetadata|FullyQualifiedName~PortfolioSqliteRepositorySyncTests.Update_PersistsPortfolioGroupId" --no-restore
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioViewModelTests.OpenSelectedPositionGroupDetail_BuildsCurrentMembershipSummary --no-restore
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioViewModelTests.OpenSelectedPositionGroupDetail_IncludesCurrentMembershipTrades --no-restore
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioViewModelTests.OpenSelectedPositionGroupDetail_BuildsCurrentMembershipTrend --no-restore
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioViewModelTests.OpenSelectedPositionGroupDetail_BuildsPerformanceTrendFromCurrentMembership --no-restore
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~AllocationViewModelTests.Constructor_WithTinyHolding_DoesNotRoundAllocationToZero --no-restore
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioViewModelTests.SaveEditAsset_WithGroupConflict_ReassignsEveryLotToSelectedGroup --no-restore
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~PortfolioViewModelTests --no-restore
dotnet build .\Assetra.WPF\Assetra.WPF.csproj --no-restore
```

## Files

- Modify: `Assetra.WPF/Features/Portfolio/PortfolioRowViewModel.cs`
  - Add conflict state for aggregated rows with mixed group IDs.
  - Update comments so `PortfolioGroupId` is clearly sourced from `PortfolioEntry`.
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`
  - Stop assigning visible row groups from latest buy trades.
  - Assign group IDs during `ApplyPositions`.
  - Preserve group/conflict fields during in-place row updates.
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Filtering.cs`
  - Add investment position view mode (`All`, `Group`, `Market`, `Type`).
  - Apply `ICollectionView.GroupDescriptions` for the selected mode.
- Modify: `Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml`
  - Replace group filter chip row with compact view-mode tabs plus secondary filters.
  - Add DataGrid/ListBox `GroupStyle` headers for group, market, and type modes.
- Modify: `Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.cs`
  - Let `OpenTxDialogForPosition` pass the asset row group as the dialog group context.
  - Ensure async group catalog loading does not reset asset-context group to default.
- Modify: `Assetra.Tests/WPF/PortfolioViewModelTests.cs`
  - Add regression tests for entry-sourced group assignment and mixed-group conflicts.
- Modify: `Assetra.Tests/WPF/TransactionDialogViewModelTests.cs`
  - Add regression test for asset-context group preselection.
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`
  - Add labels: `全部`, `依群組`, `依市場`, `依類型`, `未分組`, `群組待整理`.
- Modify: `Assetra.WPF/Languages/en-US.xaml`
  - Add matching English labels.

---

### Task 1: Row Group Source Of Truth

**Files:**
- Modify: `Assetra.Tests/WPF/PortfolioViewModelTests.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioRowViewModel.cs`
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.cs`

- [x] **Step 1: Write failing tests**

Add two tests near the existing `LoadAsync` tests:

```csharp
[Fact]
public async Task LoadAsync_UsesPortfolioEntryGroupForPositionRow()
{
    var groupId = Guid.NewGuid();
    var entry = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", PortfolioGroupId: groupId);
    var snapshots = SnapshotsFor([(entry, 35m, 1000)]);
    var (vm, _) = CreateVm([entry], PositionQueryMock(snapshots).Object);

    await vm.LoadAsync();

    var row = Assert.Single(vm.Positions);
    Assert.Equal(groupId, row.PortfolioGroupId);
    Assert.False(row.HasPortfolioGroupConflict);
}

[Fact]
public async Task LoadAsync_MarksAggregatedPositionWhenEntryGroupsConflict()
{
    var firstGroup = Guid.NewGuid();
    var secondGroup = Guid.NewGuid();
    var first = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", PortfolioGroupId: firstGroup);
    var second = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", PortfolioGroupId: secondGroup);
    var snapshots = SnapshotsFor([(first, 35m, 1000), (second, 36m, 1000)]);
    var (vm, _) = CreateVm([first, second], PositionQueryMock(snapshots).Object);

    await vm.LoadAsync();

    var row = Assert.Single(vm.Positions);
    Assert.Null(row.PortfolioGroupId);
    Assert.True(row.HasPortfolioGroupConflict);
}
```

- [x] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter "FullyQualifiedName~PortfolioViewModelTests.LoadAsync_UsesPortfolioEntryGroupForPositionRow|FullyQualifiedName~PortfolioViewModelTests.LoadAsync_MarksAggregatedPositionWhenEntryGroupsConflict" --no-restore
```

Expected: first test fails because row group is not assigned from `PortfolioEntry`; second fails because `HasPortfolioGroupConflict` does not exist.

- [x] **Step 3: Add row conflict state**

In `PortfolioRowViewModel`, replace the current `PortfolioGroupId` comment and add:

```csharp
/// <summary>
/// Position-level investment group. Source of truth is PortfolioEntry.PortfolioGroupId.
/// Trade.PortfolioGroupId is historical transaction context and must not overwrite this.
/// null means legacy/unassigned or a mixed-group aggregate that needs resolution.
/// </summary>
public Guid? PortfolioGroupId { get; set; }

public bool HasPortfolioGroupConflict { get; set; }
```

- [x] **Step 4: Assign row groups from entries**

In `PortfolioViewModel.ApplyPositions`, after `lots` is materialized, compute:

```csharp
var groupIds = lots
    .Where(l => l.IsActive)
    .Select(l => l.PortfolioGroupId ?? PortfolioGroup.DefaultId)
    .Distinct()
    .ToList();
var hasGroupConflict = groupIds.Count > 1;
var rowGroupId = hasGroupConflict ? null : groupIds.FirstOrDefault();
```

After `row = ToRow(...)` in both single-lot and aggregate branches, assign:

```csharp
row.PortfolioGroupId = rowGroupId == Guid.Empty ? PortfolioGroup.DefaultId : rowGroupId;
row.HasPortfolioGroupConflict = hasGroupConflict;
```

In the existing-row update block, copy:

```csharp
existing.PortfolioGroupId = newRow.PortfolioGroupId;
existing.HasPortfolioGroupConflict = newRow.HasPortfolioGroupConflict;
```

Remove the `latestGroup` dictionary and assignment from `ApplyLatestTradeDiscounts`.

- [x] **Step 5: Run tests and verify pass**

Run the same filtered test command. Expected: both tests pass.

---

### Task 2: Asset Context Preserves Group In Transaction Dialog

**Files:**
- Modify: `Assetra.Tests/WPF/TransactionDialogViewModelTests.cs`
- Modify: `Assetra.WPF/Features/Portfolio/SubViewModels/TransactionDialogViewModel.cs`

- [x] **Step 1: Write failing test**

Add this test near `OpenTxDialogForPosition_BuyPreselectsAssetAndLocksAssetSelector`:

```csharp
[Fact]
public void OpenTxDialogForPosition_UsesPositionGroupWhenCatalogIsLoaded()
{
    var groupId = Guid.NewGuid();
    var group = new PortfolioGroup(groupId, "長期投資");
    var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true), group]));
    catalog.RefreshAsync().GetAwaiter().GetResult();
    var position = MakePosition();
    position.PortfolioGroupId = groupId;
    var vm = CreateVm(
        positions: new ObservableCollection<PortfolioRowViewModel> { position },
        groupCatalog: catalog);

    vm.OpenTxDialogForPosition(position, "buy");

    Assert.Equal(groupId, vm.SelectedPortfolioGroup?.Id);
}
```

Add this small fake repo inside the test class:

```csharp
private sealed class FakePortfolioGroupRepo(IReadOnlyList<PortfolioGroup> groups) : IPortfolioGroupRepository
{
    public Task<IReadOnlyList<PortfolioGroup>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(groups);
    public Task<PortfolioGroup?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(groups.FirstOrDefault(g => g.Id == id));
    public Task AddAsync(PortfolioGroup group, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateAsync(PortfolioGroup group, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
}
```

Update the local `CreateVm` helper with optional `PortfolioGroupCatalog? groupCatalog = null` and pass it into `TransactionDialogDependencies`.

- [x] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter FullyQualifiedName~TransactionDialogViewModelTests.OpenTxDialogForPosition_UsesPositionGroupWhenCatalogIsLoaded --no-restore
```

Expected: fails because `OpenTxDialogForPosition` opens with default group.

- [x] **Step 3: Implement preferred group loading**

Change:

```csharp
private async Task EnsureGroupsLoadedAsync(Guid? restoreFromEditTradeId)
```

to:

```csharp
private async Task EnsureGroupsLoadedAsync(Guid? restoreFromEditTradeId, Guid? preferredGroupId = null)
```

and choose target ID in this order:

```csharp
Guid? targetId = preferredGroupId;
if (restoreFromEditTradeId is { } editId)
{
    var trade = Trades.FirstOrDefault(t => t.Id == editId);
    targetId = trade?.PortfolioGroupId ?? preferredGroupId;
}
SelectedPortfolioGroup = GroupCatalog.FindById(targetId) ?? GroupCatalog.Default;
```

Change `OpenTxDialog()` to accept an optional preferred group:

```csharp
internal void OpenTxDialog(Guid? preferredGroupId = null)
```

and call:

```csharp
_ = EnsureGroupsLoadedAsync(restoreFromEditTradeId: null, preferredGroupId);
```

Change `OpenTxDialogForPosition` to:

```csharp
OpenTxDialog(row.PortfolioGroupId);
```

- [x] **Step 4: Run test and verify pass**

Run the same filtered test command. Expected: pass.

---

### Task 3: Investment Position View Modes

**Files:**
- Modify: `Assetra.WPF/Features/Portfolio/PortfolioViewModel.Filtering.cs`
- Modify: `Assetra.WPF/Features/Portfolio/Controls/PositionsTabPanel.xaml`
- Modify: `Assetra.WPF/Languages/zh-TW.xaml`
- Modify: `Assetra.WPF/Languages/en-US.xaml`

- [x] **Step 1: Add view-mode enum and command**

Add near the top of `PortfolioViewModel.Filtering.cs`:

```csharp
public enum InvestmentPositionViewMode
{
    All,
    Group,
    Market,
    Type,
}
```

Inside `PortfolioViewModel`, add:

```csharp
[ObservableProperty] private InvestmentPositionViewMode _positionViewMode = InvestmentPositionViewMode.All;

partial void OnPositionViewModeChanged(InvestmentPositionViewMode value)
{
    ApplyPositionViewGrouping();
    PositionsView.Refresh();
}

[RelayCommand]
private void SetPositionViewMode(InvestmentPositionViewMode mode) => PositionViewMode = mode;
```

- [x] **Step 2: Apply collection grouping**

Add:

```csharp
private void ApplyPositionViewGrouping()
{
    if (_positionsView is null) return;
    using (_positionsView.DeferRefresh())
    {
        _positionsView.GroupDescriptions.Clear();
        var propertyName = PositionViewMode switch
        {
            InvestmentPositionViewMode.Group => nameof(PortfolioRowViewModel.PortfolioGroupDisplay),
            InvestmentPositionViewMode.Market => nameof(PortfolioRowViewModel.MarketDisplay),
            InvestmentPositionViewMode.Type => nameof(PortfolioRowViewModel.AssetTypeDisplay),
            _ => null,
        };
        if (propertyName is not null)
            _positionsView.GroupDescriptions.Add(new PropertyGroupDescription(propertyName));
    }
}
```

Call `ApplyPositionViewGrouping()` when `PositionsView` is created.

- [x] **Step 3: Add display properties on row**

Add to `PortfolioRowViewModel`:

```csharp
public string PortfolioGroupDisplay { get; set; } = "未分組";
public string MarketDisplay => string.IsNullOrWhiteSpace(Exchange) ? Currency : Exchange;
public string AssetTypeDisplay => AssetType switch
{
    AssetType.Etf => "ETF",
    AssetType.Fund => "基金",
    AssetType.Bond => "債券",
    AssetType.Crypto => "加密",
    AssetType.PreciousMetal => "貴金屬",
    _ => IsEtf ? "ETF" : "個股",
};
```

In `ApplyPositions`, after group ID assignment, resolve display name:

```csharp
row.PortfolioGroupDisplay = hasGroupConflict
    ? "群組待整理"
    : GroupCatalog?.FindById(row.PortfolioGroupId)?.Name ?? "未分組";
```

- [x] **Step 4: Update XAML controls**

Above the current filter chips, add compact tabs:

```xml
<StackPanel Orientation="Horizontal" Margin="12,12,12,8">
    <RadioButton Command="{Binding SetPositionViewModeCommand}" CommandParameter="{x:Static local:InvestmentPositionViewMode.All}" Content="{DynamicResource Portfolio.ViewMode.All}" Style="{StaticResource SegmentRadioButton}" />
    <RadioButton Command="{Binding SetPositionViewModeCommand}" CommandParameter="{x:Static local:InvestmentPositionViewMode.Group}" Content="{DynamicResource Portfolio.ViewMode.Group}" Style="{StaticResource SegmentRadioButton}" />
    <RadioButton Command="{Binding SetPositionViewModeCommand}" CommandParameter="{x:Static local:InvestmentPositionViewMode.Market}" Content="{DynamicResource Portfolio.ViewMode.Market}" Style="{StaticResource SegmentRadioButton}" />
    <RadioButton Command="{Binding SetPositionViewModeCommand}" CommandParameter="{x:Static local:InvestmentPositionViewMode.Type}" Content="{DynamicResource Portfolio.ViewMode.Type}" Style="{StaticResource SegmentRadioButton}" />
</StackPanel>
```

Ensure the XAML root has:

```xml
xmlns:local="clr-namespace:Assetra.WPF.Features.Portfolio"
```

Add `GroupStyle` to both `ListBox` and `DataGrid` with a compact header binding to `{Binding Name}`.

- [x] **Step 5: Add language resources**

Add:

```xml
<sys:String x:Key="Portfolio.ViewMode.All">全部</sys:String>
<sys:String x:Key="Portfolio.ViewMode.Group">依群組</sys:String>
<sys:String x:Key="Portfolio.ViewMode.Market">依市場</sys:String>
<sys:String x:Key="Portfolio.ViewMode.Type">依類型</sys:String>
<sys:String x:Key="Portfolio.Group.Ungrouped">未分組</sys:String>
<sys:String x:Key="Portfolio.Group.NeedsResolution">群組待整理</sys:String>
```

and English equivalents.

- [x] **Step 6: Build**

Run:

```powershell
dotnet build .\Assetra.WPF\Assetra.WPF.csproj --no-restore
```

Expected: build succeeds with no XAML parse/resource errors.

---

## Verification

Run after this slice:

```powershell
dotnet test .\Assetra.Tests\Assetra.Tests.csproj --filter "FullyQualifiedName~PortfolioViewModelTests.LoadAsync_UsesPortfolioEntryGroupForPositionRow|FullyQualifiedName~PortfolioViewModelTests.LoadAsync_MarksAggregatedPositionWhenEntryGroupsConflict|FullyQualifiedName~TransactionDialogViewModelTests.OpenTxDialogForPosition_UsesPositionGroupWhenCatalogIsLoaded" --no-restore
dotnet build .\Assetra.WPF\Assetra.WPF.csproj --no-restore
```

Manual QA:

- Investment assets page opens.
- Existing flat list still works.
- `依群組`, `依市場`, `依類型` switch without blanking the grid.
- Opening buy/sell/dividend from an asset side panel does not ask the user to reselect the asset.
- If the asset has a group, the transaction dialog uses that group.
- Rebalance uses the same compact panel language as the allocation overview: thin command bar, pill mode selector, and a compact cash-flow input strip.
- Editing a mixed-group aggregate shows the group conflict warning and assigning a group updates every visible lot.

## Known Follow-Up After This Slice

- Historical-membership group performance reports; V1 currently uses current membership for the group detail performance trend.
- Full lot-level split/merge conflict resolver for aggregated rows that contain mixed lot groups; the V1 edit dialog currently supports an explicit warning plus assign-all behavior.
