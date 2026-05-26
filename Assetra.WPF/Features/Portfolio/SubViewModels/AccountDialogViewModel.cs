using System.Collections.ObjectModel;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.PortfolioGroups;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Dependencies passed to <see cref="AccountDialogViewModel"/> at construction.
/// Bundles services, shared collections, and Func/Action callbacks so the sub-VM
/// can mutate parent state without holding a back-reference to
/// <see cref="Portfolio.PortfolioViewModel"/>.
/// </summary>
internal sealed record AccountDialogDependencies(
    IAccountUpsertWorkflowService AccountUpsert,
    IAccountMutationWorkflowService AccountMutation,
    IPositionMetadataWorkflowService PositionMetadata,
    PortfolioGroupCatalog? GroupCatalog,
    ISnackbarService? Snackbar,
    ReadOnlyObservableCollection<CashAccountRowViewModel> CashAccounts,
    Func<Task> LoadCashAccountsAsync,
    Func<Guid?, Task> ApplyDefaultCashAccountAsync,
    Action<string, Func<Task>> AskConfirm,
    Action RebuildTotals,
    Func<string, string, string> Localize);

/// <summary>
/// Owns the edit-asset dialog state and account-management commands
/// (archive, delete, default-cash toggle). Raised <see cref="AccountChanged"/>
/// after mutations so the parent <see cref="PortfolioViewModel"/> can reload
/// cash accounts, balances, and totals.
/// </summary>
public partial class AccountDialogViewModel : ObservableObject
{
    private readonly IAccountUpsertWorkflowService _accountUpsert;
    private readonly IAccountMutationWorkflowService _accountMutation;
    private readonly IPositionMetadataWorkflowService _positionMetadata;
    private readonly PortfolioGroupCatalog? _groupCatalog;
    private readonly ISnackbarService? _snackbar;
    private readonly ReadOnlyObservableCollection<CashAccountRowViewModel> _cashAccounts;
    private readonly Func<Task> _loadCashAccountsAsync;
    private readonly Func<Guid?, Task> _applyDefaultCashAccountAsync;
    private readonly Action<string, Func<Task>> _askConfirm;
    private readonly Action _rebuildTotals;
    private readonly Func<string, string, string> _localize;

    // Private edit-dialog state (not bound by XAML directly, set before opening)
    private string _editAssetKind = string.Empty;
    private Guid _editAssetId;
    private PortfolioRowViewModel? _editPositionRow;

    /// <summary>
    /// Raised after every successful account mutation (archive, delete) or asset rename
    /// so the parent ViewModel can reload accounts and rebuild totals via
    /// <c>ReloadAfterAccountChangedAsync</c>.
    /// </summary>
    public event EventHandler? AccountChanged;

    internal AccountDialogViewModel(AccountDialogDependencies deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        _accountUpsert = deps.AccountUpsert;
        _accountMutation = deps.AccountMutation;
        _positionMetadata = deps.PositionMetadata;
        _groupCatalog = deps.GroupCatalog;
        _snackbar = deps.Snackbar;
        _cashAccounts = deps.CashAccounts;
        _loadCashAccountsAsync = deps.LoadCashAccountsAsync;
        _applyDefaultCashAccountAsync = deps.ApplyDefaultCashAccountAsync;
        _askConfirm = deps.AskConfirm;
        _rebuildTotals = deps.RebuildTotals;
        _localize = deps.Localize;
    }

    // ── Edit Asset dialog ─────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isEditAssetDialogOpen;
    [ObservableProperty] private string _editAssetDialogTitle = string.Empty;
    [ObservableProperty] private string _editAssetDialogSubtitle = string.Empty;
    [ObservableProperty] private string _editAssetName = string.Empty;
    [ObservableProperty] private string _editAssetTypeLabel = string.Empty;
    [ObservableProperty] private string _editAssetError = string.Empty;
    [ObservableProperty] private string _editAssetSymbol = string.Empty;
    [ObservableProperty] private string _editAssetCurrency = "TWD";
    [ObservableProperty] private bool _editAssetIsStock;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditPositionGroup))]
    private bool _editAssetIsPosition;
    [ObservableProperty] private bool _editAssetHasPortfolioGroupConflict;
    [ObservableProperty] private Guid _editAssetPortfolioGroupId = PortfolioGroup.DefaultId;

    /// <summary>True 表示目前編輯的是現金類帳戶（cash），可改 Subtype。</summary>
    [ObservableProperty] private bool _editAssetIsCash;

    public IEnumerable<PortfolioGroup> PortfolioGroupOptions =>
        _groupCatalog?.Groups ?? Enumerable.Empty<PortfolioGroup>();

    public bool CanEditPositionGroup =>
        EditAssetIsPosition && _groupCatalog?.Groups.Any(group => !group.IsSystem) == true;

    /// <summary>使用者可改的 Subtype（細分類）— 對應 AddAssetDialog 的 9 種選項；
    /// 「自訂」對應到任意自訂字串，但目前 dialog 用下拉選單只給預設清單。</summary>
    [ObservableProperty] private string _editAssetSubtype = string.Empty;

    /// <summary>下拉選單來源 — 與 AddAssetDialog 對應的 cash subtype 預設清單。</summary>
    public static IReadOnlyList<string> CashSubtypeOptions { get; } =
    [
        "手邊現金",
        "銀行活存",
        "數位活存",
        "定期存款",
        "外幣活存",
        "證券交割戶",
        "電子支付",
        "儲值卡",
    ];

    /// <summary>Supported currency options for the edit-asset currency picker.</summary>
    public static IReadOnlyList<CurrencyOption> SupportedCurrencies { get; } =
    [
        new("TWD", "TWD (NT$)"),
        new("USD", "USD ($)"),
        new("EUR", "EUR (€)"),
        new("JPY", "JPY (¥)"),
        new("GBP", "GBP (£)"),
        new("HKD", "HKD (HK$)"),
    ];

    [RelayCommand]
    private void OpenEditCash(CashAccountRowViewModel row)
    {
        _editAssetKind = "cash";
        _editAssetId = row.Id;
        EditAssetDialogTitle = L("Portfolio.EditAsset.CashTitle", "編輯資金帳戶");
        EditAssetDialogSubtitle = L("Portfolio.EditAsset.CashSubtitle", "更新帳戶詳細資訊。");
        EditAssetName = row.Name;
        EditAssetCurrency = row.Currency;
        EditAssetTypeLabel = L("Portfolio.Dialog.TypeCash", "資金帳戶");
        EditAssetIsCash = true;
        EditAssetIsPosition = false;
        EditAssetHasPortfolioGroupConflict = false;
        // 預設帶入既有 subtype；若該 subtype 不在預設清單（例如「自訂」），下拉框會顯示空白，
        // 但儲存時會保留 null（不改原值），避免覆蓋使用者自訂 label。
        EditAssetSubtype = CashSubtypeOptions.Contains(row.Subtype ?? string.Empty)
            ? row.Subtype!
            : string.Empty;
        EditAssetError = string.Empty;
        EditAssetIsStock = false;
        IsEditAssetDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditPosition(PortfolioRowViewModel row)
    {
        _editAssetKind = "position";
        _editPositionRow = row;
        EditAssetDialogTitle = L("Portfolio.EditAsset.PositionTitle", "編輯資產");
        EditAssetDialogSubtitle = L("Portfolio.EditAsset.PositionSubtitle", "更新投資資產名稱、幣別與群組。");
        EditAssetName = string.IsNullOrWhiteSpace(row.Name) ? row.Symbol : row.Name;
        EditAssetSymbol = row.Symbol;
        EditAssetCurrency = row.Currency;
        EditAssetTypeLabel = L("Portfolio.Dialog.Type" + row.AssetType, row.AssetType.ToString());
        EditAssetIsCash = false;
        EditAssetIsPosition = true;
        EditAssetHasPortfolioGroupConflict = row.HasPortfolioGroupConflict;
        EditAssetSubtype = string.Empty;
        EditAssetIsStock = row.IsStock;
        EditAssetPortfolioGroupId = row.HasPortfolioGroupConflict
            ? PortfolioGroup.DefaultId
            : row.PortfolioGroupId ?? PortfolioGroup.DefaultId;
        EditAssetError = string.Empty;
        IsEditAssetDialogOpen = true;
    }

    [RelayCommand]
    private void CloseEditAsset()
    {
        IsEditAssetDialogOpen = false;
        EditAssetError = string.Empty;
        EditAssetHasPortfolioGroupConflict = false;
    }

    [RelayCommand]
    private async Task SaveEditAsset()
    {
        EditAssetError = string.Empty;
        if (string.IsNullOrWhiteSpace(EditAssetName))
        { EditAssetError = L("Portfolio.Dialog.NameCannotBeEmpty", "名稱不得空白"); return; }

        var name = EditAssetName.Trim();
        var currency = EditAssetCurrency;

        if (_editAssetKind == "cash")
        {
            var row = _cashAccounts.FirstOrDefault(r => r.Id == _editAssetId);
            if (row is not null)
            {
                // Subtype 為空字串 = 使用者沒選（如自訂類） → 保留原值；有選 → 套新值。
                // AccountUpsertWorkflowService 內 ResolveGroupIdForSubtype 會自動更新 GroupId。
                var subtypeToSave = string.IsNullOrWhiteSpace(EditAssetSubtype) ? row.Subtype : EditAssetSubtype;
                Serilog.Log.Information(
                    "[AccountDialog] SaveEditAsset cash: id={Id} name={Name} oldSubtype={Old} newSubtype={New} dropdownVal={Drop}",
                    row.Id, name, row.Subtype, subtypeToSave, EditAssetSubtype);
                try
                {
                    var result = await _accountUpsert.UpdateAsync(new UpdateAccountRequest(
                        row.Id,
                        name,
                        currency,
                        row.CreatedDate,
                        Subtype: subtypeToSave));
                    Serilog.Log.Information(
                        "[AccountDialog] SaveEditAsset succeeded: persistedSubtype={Persisted}",
                        result.Account.Subtype);
                    row.Name = result.Account.Name;
                    row.Currency = result.Account.Currency;
                    row.Subtype = result.Account.Subtype;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "[AccountDialog] SaveEditAsset cash UPDATE failed for {Id}", row.Id);
                    EditAssetError = ex.Message;
                    return;  // 不要關 dialog，讓使用者看到錯誤
                }
            }
        }
        else if (_editAssetKind == "position" && _editPositionRow is { } posRow)
        {
            var entryIds = posRow.AllEntryIds.ToList();
            await _positionMetadata.UpdateAsync(new PositionMetadataUpdateRequest(
                entryIds,
                name,
                currency));
            if (CanEditPositionGroup)
            {
                await _positionMetadata.UpdateGroupAsync(new PositionGroupUpdateRequest(
                    entryIds,
                    EditAssetPortfolioGroupId));
            }
            posRow.Name = name;
            posRow.Currency = currency;
            if (CanEditPositionGroup)
            {
                posRow.PortfolioGroupId = EditAssetPortfolioGroupId;
                posRow.HasPortfolioGroupConflict = false;
                posRow.PortfolioGroupDisplay = ResolvePortfolioGroupDisplay(EditAssetPortfolioGroupId);
            }
        }

        IsEditAssetDialogOpen = false;
        EditAssetHasPortfolioGroupConflict = false;
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Account management commands ───────────────────────────────────────────────────

    [RelayCommand]
    private void ArchiveAccount(CashAccountRowViewModel? row)
    {
        if (row is null || _accountMutation is null)
            return;
        var msg = L("Portfolio.Confirm.ArchiveAccount", "確定封存此帳戶？（餘額保留；交易紀錄保留）");
        _askConfirm(msg, async () =>
        {
            await _accountMutation.ArchiveAsync(row.Id);
            if (DefaultCashAccountId == row.Id)
                await _applyDefaultCashAccountAsync(null);
            AccountChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// 依目前 IsActive 狀態 toggle：active → 封存（需確認）；archived → 取消封存（直接執行不需確認）。
    /// 取消封存不需確認因為它本來就是還原動作；封存確認是因為使用者可能誤點。
    /// </summary>
    [RelayCommand]
    private void ToggleArchive(CashAccountRowViewModel? row)
    {
        if (row is null || _accountMutation is null)
            return;

        if (row.IsActive)
        {
            // active → 封存（需確認）
            ArchiveAccount(row);
        }
        else
        {
            // archived → 取消封存（直接做）
            _ = ExecuteUnarchiveAsync(row.Id);
        }
    }

    private async Task ExecuteUnarchiveAsync(Guid id)
    {
        if (_accountMutation is null)
            return;
        await _accountMutation.UnarchiveAsync(id);
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private string ResolvePortfolioGroupDisplay(Guid groupId)
    {
        if (groupId == PortfolioGroup.DefaultId)
            return L("Portfolio.Group.Ungrouped", "未分組");

        return _groupCatalog?.FindById(groupId)?.Name
            ?? L("Portfolio.Group.Ungrouped", "未分組");
    }

    /// <summary>
    /// Permanent hard-delete. Guarded: rejects if any trade row references this account.
    /// </summary>
    [RelayCommand]
    private void RemoveCashAccount(CashAccountRowViewModel? row)
    {
        if (row is null || _accountMutation is null)
            return;

        _askConfirm(
            L("Portfolio.Confirm.DeleteAccount", "確定刪除此帳戶？"),
            async () =>
            {
                await _accountMutation.DeleteAsync(row.Id);
                // M6-B — _cashAccounts is now read-only; parent VM removes the row
                // via Internal_RemoveCashAccount in its AccountChanged handler.
                AccountChanged?.Invoke(this, EventArgs.Empty);

                if (DefaultCashAccountId == row.Id)
                    await _applyDefaultCashAccountAsync(null);

                _rebuildTotals();
            });
    }

    /// <summary>目前設為預設的現金帳戶 Id（從 AppSettings 載入）。</summary>
    [ObservableProperty] private Guid? _defaultCashAccountId;

    /// <summary>將指定帳戶設為預設，並持久化到 AppSettings。</summary>
    [RelayCommand]
    private async Task SetAsDefaultCashAccount(CashAccountRowViewModel row)
    {
        if (row is null)
            return;
        await _applyDefaultCashAccountAsync(row.Id);
    }

    /// <summary>清除目前的預設現金帳戶。</summary>
    [RelayCommand]
    private async Task ClearDefaultCashAccount()
    {
        await _applyDefaultCashAccountAsync(null);
    }

    /// <summary>
    /// 切換指定帳戶的預設狀態：已是預設 → 清除；不是 → 設為預設。
    /// </summary>
    [RelayCommand]
    private async Task ToggleDefaultCashAccount(CashAccountRowViewModel row)
    {
        if (row is null)
            return;
        await _applyDefaultCashAccountAsync(row.IsDefault ? null : row.Id);
    }

    /// <summary>
    /// When true, archived (soft-deleted) accounts are shown in the Cash list.
    /// </summary>
    [ObservableProperty] private bool _showArchivedAccounts;

    partial void OnShowArchivedAccountsChanged(bool value)
        => _ = _loadCashAccountsAsync();

    // ── Private helpers ───────────────────────────────────────────────────────────────

    private string L(string key, string fallback = "") =>
        _localize(key, fallback);
}
