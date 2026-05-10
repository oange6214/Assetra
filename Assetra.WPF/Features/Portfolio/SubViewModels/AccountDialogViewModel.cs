using System.Collections.ObjectModel;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
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
    [ObservableProperty] private string _editAssetName = string.Empty;
    [ObservableProperty] private string _editAssetTypeLabel = string.Empty;
    [ObservableProperty] private string _editAssetError = string.Empty;
    [ObservableProperty] private string _editAssetSymbol = string.Empty;
    [ObservableProperty] private string _editAssetCurrency = "TWD";
    [ObservableProperty] private bool _editAssetIsStock;

    /// <summary>True 表示目前編輯的是現金類帳戶（cash），可改 Subtype。</summary>
    [ObservableProperty] private bool _editAssetIsCash;

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
        EditAssetName = row.Name;
        EditAssetCurrency = row.Currency;
        EditAssetTypeLabel = L("Portfolio.Dialog.TypeCash", "資金帳戶");
        EditAssetIsCash = true;
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
        EditAssetName = row.Name;
        EditAssetSymbol = row.Symbol;
        EditAssetCurrency = row.Currency;
        EditAssetTypeLabel = L("Portfolio.Dialog.Type" + row.AssetType, row.AssetType.ToString());
        EditAssetIsCash = false;
        EditAssetSubtype = string.Empty;
        EditAssetIsStock = row.IsStock;
        EditAssetError = string.Empty;
        IsEditAssetDialogOpen = true;
    }

    [RelayCommand]
    private void CloseEditAsset()
    {
        IsEditAssetDialogOpen = false;
        EditAssetError = string.Empty;
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
                var result = await _accountUpsert.UpdateAsync(new UpdateAccountRequest(
                    row.Id,
                    name,
                    currency,
                    row.CreatedDate,
                    Subtype: subtypeToSave));
                row.Name = result.Account.Name;
                row.Currency = result.Account.Currency;
                row.Subtype = result.Account.Subtype;
            }
        }
        else if (_editAssetKind == "position" && _editPositionRow is { } posRow)
        {
            await _positionMetadata.UpdateAsync(new PositionMetadataUpdateRequest(
                posRow.AllEntryIds.ToList(),
                name,
                currency));
            posRow.Name = name;
            posRow.Currency = currency;
        }

        IsEditAssetDialogOpen = false;
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
