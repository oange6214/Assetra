using System.IO;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.PortfolioGroups;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// Portfolio-Groups-Refactor P2 — CRUD VM command coverage. Backs the real SQLite repo
/// (not a mock) so the system-protect guard + catalog refresh interactions are exercised
/// end-to-end. One temp DB file per test for isolation.
/// </summary>
public sealed class PortfolioGroupsViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IPortfolioGroupRepository _repo;
    private readonly PortfolioGroupCatalog _catalog;
    private readonly PortfolioGroupsViewModel _vm;

    public PortfolioGroupsViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pgroup_vm_{Guid.NewGuid():N}.db");
        _repo = new PortfolioGroupSqliteRepository(_dbPath);
        _catalog = new PortfolioGroupCatalog(_repo);
        _vm = new PortfolioGroupsViewModel(_repo, _catalog);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void CloseDialog_ResetsDialogAndForm()
    {
        // The management page is now a shell modal: closing it must also collapse any open
        // add/edit sub-form so re-opening starts clean.
        _vm.IsDialogOpen = true;
        _vm.StartAddCommand.Execute(null);
        Assert.True(_vm.IsFormOpen);

        _vm.CloseDialogCommand.Execute(null);

        Assert.False(_vm.IsDialogOpen);
        Assert.False(_vm.IsFormOpen);
    }

    [Fact]
    public void OpenRequestEvent_OpensDialog()
    {
        // 取代整頁導覽：RequestOpenPortfolioGroups 觸發 → VM 設 IsDialogOpen=true（MainWindow 覆蓋顯示）。
        Assert.False(_vm.IsDialogOpen);
        Assetra.WPF.Infrastructure.ShellNavigationEvents.RequestOpenPortfolioGroups();
        Assert.True(_vm.IsDialogOpen);
    }

    [Fact]
    public async Task LoadAsync_PopulatesSeededDefaultGroup()
    {
        await _vm.LoadAsync();

        Assert.True(_vm.IsLoaded);
        Assert.True(_vm.HasGroups);
        var def = Assert.Single(_vm.Groups);
        Assert.Equal(PortfolioGroup.DefaultId, def.Id);
        Assert.True(def.IsSystem);
    }

    [Fact]
    public async Task Save_NewGroup_AddsRowAndRefreshesCatalog()
    {
        await _vm.LoadAsync();

        _vm.StartAddCommand.Execute(null);
        _vm.FormName = "退休帳戶";
        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(2, _vm.Groups.Count);
        Assert.False(_vm.IsFormOpen);
        Assert.Null(_vm.FormError);
        Assert.Contains(_vm.Groups, g => g.Name == "退休帳戶" && !g.IsSystem);
        // catalog refresh is wired — the new group must surface through the shared catalog too
        Assert.Equal(2, _catalog.Groups.Count);
        Assert.NotNull(_catalog.Groups.FirstOrDefault(g => g.Name == "退休帳戶"));
    }

    [Fact]
    public async Task Save_EmptyName_SetsFormErrorAndKeepsFormOpen()
    {
        await _vm.LoadAsync();
        _vm.StartAddCommand.Execute(null);

        _vm.FormName = "   ";
        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.True(_vm.IsFormOpen);
        Assert.NotNull(_vm.FormError);
        Assert.Equal(1, _vm.Groups.Count); // still just the default
    }

    [Fact]
    public async Task StartEdit_PopulatesFormWithExistingRowFields()
    {
        // WHY: StartEdit must open the form with the row's name pre-filled so the
        // user can rename without clearing the field; EditingId must be set so
        // SaveAsync routes to UpdateAsync rather than AddAsync.
        await _vm.LoadAsync();
        _vm.StartAddCommand.Execute(null);
        _vm.FormName = "買房儲蓄";
        await _vm.SaveCommand.ExecuteAsync(null);

        var newRow = _vm.Groups.First(g => g.Name == "買房儲蓄");
        _vm.StartEditCommand.Execute(newRow);

        Assert.True(_vm.IsFormOpen);
        Assert.True(_vm.IsEditing);
        Assert.Equal(newRow.Id, _vm.EditingId);
        Assert.Equal("買房儲蓄", _vm.FormName);
    }

    [Fact]
    public async Task StartAdd_SetsIsAddingNew_AndLeavesNoRowInInlineEdit()
    {
        // WHY: 就地新增的頂端輸入列由 IsAddingNew 控制（= IsFormOpen && !IsEditing）；
        // 開啟「新增」時不可有任何既有列誤入就地編輯模式（否則畫面會同時冒出兩個輸入）。
        await _vm.LoadAsync();

        _vm.StartAddCommand.Execute(null);

        Assert.True(_vm.IsAddingNew);
        Assert.False(_vm.IsEditing);
        Assert.All(_vm.Groups, g => Assert.False(g.IsBeingEdited));
    }

    [Fact]
    public async Task StartEdit_MarksOnlyTargetRowInline_AndNotAddingNew()
    {
        // WHY: 就地編輯時只有被點的那一列 IsBeingEdited=true（其餘 false），row template
        // 才會只在該列把名稱換成輸入框；同時不能是「新增」狀態（頂端列不應一起冒出）。
        await _vm.LoadAsync();
        _vm.StartAddCommand.Execute(null);
        _vm.FormName = "退休帳戶";
        await _vm.SaveCommand.ExecuteAsync(null);
        var target = _vm.Groups.First(g => g.Name == "退休帳戶");

        _vm.StartEditCommand.Execute(target);

        Assert.False(_vm.IsAddingNew);
        Assert.True(target.IsBeingEdited);
        Assert.All(_vm.Groups.Where(g => g.Id != target.Id), g => Assert.False(g.IsBeingEdited));
    }

    [Fact]
    public async Task Cancel_ClearsInlineAddAndEditState()
    {
        // WHY: 取消（含 Esc）必須同時收起頂端新增列與任何就地編輯列，下次開啟才乾淨；
        // 殘留的 IsBeingEdited 會讓某列一直卡在輸入狀態。
        await _vm.LoadAsync();
        var def = _vm.Groups.Single();
        _vm.StartEditCommand.Execute(def);
        Assert.True(def.IsBeingEdited);

        _vm.CancelCommand.Execute(null);

        Assert.False(_vm.IsAddingNew);
        Assert.All(_vm.Groups, g => Assert.False(g.IsBeingEdited));
    }

    [Fact]
    public async Task Save_Edit_PreservesIsSystemFlagAndUpdatesRow()
    {
        // WHY: Editing the system default group must persist the name change
        // but must NOT flip IsSystem to false — that would expose a Delete button
        // for the default group and allow repo-level removal.
        await _vm.LoadAsync();
        var def = _vm.Groups.Single();
        Assert.True(def.IsSystem);

        _vm.StartEditCommand.Execute(def);
        _vm.FormName = "My Default";
        await _vm.SaveCommand.ExecuteAsync(null);

        var reloaded = await _repo.GetByIdAsync(PortfolioGroup.DefaultId);
        Assert.NotNull(reloaded);
        Assert.Equal("My Default", reloaded!.Name);
        Assert.True(reloaded.IsSystem); // must NOT be flipped off by an edit
    }

    [Fact]
    public async Task CanDelete_IsFalseForSystemDefault_AndTrueForUserGroup()
    {
        await _vm.LoadAsync();
        var def = _vm.Groups.Single();
        _vm.StartEditCommand.Execute(def);
        Assert.False(_vm.CanDelete);

        _vm.StartAddCommand.Execute(null);
        _vm.FormName = "緊急基金";
        await _vm.SaveCommand.ExecuteAsync(null);
        var userRow = _vm.Groups.First(g => !g.IsSystem);
        _vm.StartEditCommand.Execute(userRow);

        Assert.True(_vm.CanDelete);
    }

    [Fact]
    public async Task Remove_OnSystemRow_SetsErrorAndKeepsRow()
    {
        await _vm.LoadAsync();
        var def = _vm.Groups.Single(g => g.IsSystem);

        _vm.RemoveCommand.Execute(def);

        // Should short-circuit before even opening the confirm dialog.
        Assert.False(_vm.IsConfirmDialogOpen);
        Assert.NotNull(_vm.ErrorMessage);
        Assert.Single(_vm.Groups);
    }

    [Fact]
    public async Task Remove_OnUserRow_OpensConfirm_AndAfterYes_RemovesAndRefreshesCatalog()
    {
        await _vm.LoadAsync();
        _vm.StartAddCommand.Execute(null);
        _vm.FormName = "短線交易";
        await _vm.SaveCommand.ExecuteAsync(null);
        var userRow = _vm.Groups.First(g => g.Name == "短線交易");
        Assert.Equal(2, _vm.Groups.Count);

        _vm.RemoveCommand.Execute(userRow);
        Assert.True(_vm.IsConfirmDialogOpen);

        await _vm.ConfirmDialogYesCommand.ExecuteAsync(null);

        Assert.False(_vm.IsConfirmDialogOpen);
        Assert.Single(_vm.Groups);
        Assert.DoesNotContain(_vm.Groups, g => g.Name == "短線交易");
        Assert.Single(_catalog.Groups); // catalog refresh fired
    }

    [Fact]
    public async Task ConfirmDialogNo_LeavesRowIntact()
    {
        await _vm.LoadAsync();
        _vm.StartAddCommand.Execute(null);
        _vm.FormName = "暫存";
        await _vm.SaveCommand.ExecuteAsync(null);
        var userRow = _vm.Groups.First(g => g.Name == "暫存");

        _vm.RemoveCommand.Execute(userRow);
        Assert.True(_vm.IsConfirmDialogOpen);
        _vm.ConfirmDialogNoCommand.Execute(null);

        Assert.False(_vm.IsConfirmDialogOpen);
        Assert.Equal(2, _vm.Groups.Count); // still 2
    }
}
