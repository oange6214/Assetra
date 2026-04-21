using System.Reactive.Linq;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Trading;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio;

public sealed record CurrencyOption(string Code, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// Sell-trigger commands (BeginSell / BeginSellForSelectedPosition), edit-asset dialog,
/// cash/liability CRUD, GlobalAdd command, and account management.
/// Sell-panel state and CancelSell / ConfirmSell commands have been extracted to
/// <see cref="SubViewModels.SellPanelViewModel"/> (exposed as <see cref="SellPanel"/>).
/// Add-asset dialog state and commands have been extracted to
/// <see cref="SubViewModels.AddAssetDialogViewModel"/> (exposed as <see cref="AddAssetDialog"/>).
/// </summary>
public partial class PortfolioViewModel
{
    // Sell Panel commands

    [RelayCommand]
    private void BeginSell(PortfolioRowViewModel row)
    {
        // Open Tx dialog in Sell mode with this position pre-selected
        OpenTxDialog();
        TxType = "sell";
        TxSellPosition = row;
        TxSellQuantity = ((int)row.Quantity).ToString();
    }

    /// <summary>側面板「買入」快速動作 — 打開 Tx 對話框，預填當前股票代號。</summary>
    [RelayCommand]
    private void BeginBuyForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        var row = SelectedPositionRow;
        OpenTxDialog();
        TxType = "buy";
        TxBuyAssetType = "stock";
        AddAssetDialog.AddSymbol = row.Symbol;
        AddAssetDialog.AddPrice = string.Empty;
        AddAssetDialog.AddQuantity = string.Empty;
    }

    /// <summary>側面板「配息入帳」快速動作 — 打開 Tx 對話框並預選此持倉。</summary>
    [RelayCommand]
    private void BeginDividendForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        OpenTxDialog();
        TxType = "cashDiv";
        TxDivPosition = SelectedPositionRow;
    }

    /// <summary>側面板「賣出」快速動作 — 呼叫既有 BeginSell，但以 SelectedPositionRow 為目標。</summary>
    [RelayCommand]
    private void BeginSellForSelectedPosition()
    {
        if (SelectedPositionRow is null)
            return;
        BeginSell(SelectedPositionRow);
    }

    // 全域「新增交易」按鈕 — 一律開啟交易對話框
    // 建立帳戶的動作改由各 tab 內的「新增帳戶」ghost 按鈕處理，讓主要動作
    // （買/賣/存/提/借/還/股利…）統一從一個入口進入。

    [RelayCommand]
    private void GlobalAdd() => OpenTxDialog();

    /// <summary>開啟新增現金帳戶對話框（由現金 tab 的「新增帳戶」按鈕呼叫）。</summary>
    [RelayCommand]
    private void OpenAddAccountDialog()
    {
        SelectedTab = PortfolioTab.Accounts;
        AddAssetDialog.AddAssetType = "cash";
        AddAssetDialog.AddError = string.Empty;
        AddAssetDialog.AddAccountName = string.Empty;
        AddAssetDialog.IsAddDialogOpen = true;
    }

    // Edit Asset dialog — shared across Cash, Liability, and Investment positions

    [ObservableProperty] private bool _isEditAssetDialogOpen;
    [ObservableProperty] private string _editAssetName = string.Empty;
    [ObservableProperty] private string _editAssetTypeLabel = string.Empty;
    [ObservableProperty] private string _editAssetError = string.Empty;
    [ObservableProperty] private string _editAssetSymbol = string.Empty;
    [ObservableProperty] private string _editAssetCurrency = "TWD";
    [ObservableProperty] private bool _editAssetIsStock;

    private string _editAssetKind = string.Empty;
    private Guid _editAssetId;
    private PortfolioRowViewModel? _editPositionRow;

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
        EditAssetTypeLabel = L("Portfolio.Dialog.TypeCash", "現金");
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
        { EditAssetError = "名稱不得空白"; return; }

        var name = EditAssetName.Trim();

        var currency = EditAssetCurrency;

        if (_editAssetKind == "cash")
        {
            var row = CashAccounts.FirstOrDefault(r => r.Id == _editAssetId);
            if (row is not null)
            {
                var result = await _accountUpsertWorkflowService.UpdateAsync(new UpdateAccountRequest(
                    row.Id,
                    name,
                    currency,
                    row.CreatedDate));
                row.Name = result.Account.Name;
                row.Currency = result.Account.Currency;
            }
        }
        else if (_editAssetKind == "position" && _editPositionRow is { } posRow)
        {
            await _positionMetadataWorkflowService.UpdateAsync(new PositionMetadataUpdateRequest(
                posRow.AllEntryIds.ToList(),
                name,
                currency));
            posRow.Name = name;
            posRow.Currency = currency;
        }

        IsEditAssetDialogOpen = false;
    }

    [RelayCommand]
    private void ArchiveAccount(CashAccountRowViewModel? row)
    {
        if (row is null || _assetRepo is null)
            return;
        var msg = L("Portfolio.Confirm.ArchiveAccount", "確定封存此帳戶？（餘額保留；交易紀錄保留）");
        AskConfirm(msg, async () =>
        {
            await _accountMutationWorkflowService.ArchiveAsync(row.Id);
            await LoadCashAccountsAsync();
            if (DefaultCashAccountId == row.Id)
                await ApplyDefaultCashAccountAsync(null);
            RebuildTotals();
        });
    }

    /// <summary>
    /// Permanent hard-delete. Guarded: rejects if any trade row references this account.
    /// Plan Task 14 — message uses {0} count for clarity; navigation-to-trades shortcut
    /// is deferred (plan flags it as acceptable fallback).
    /// </summary>
    [RelayCommand]
    private void RemoveCashAccount(CashAccountRowViewModel? row)
    {
        if (row is null || _assetRepo is null)
            return;

        AskConfirm(
            L("Portfolio.Confirm.DeleteAccount", "確定刪除此帳戶？"),
            async () =>
            {
                var result = await _accountMutationWorkflowService.DeleteAsync(row.Id);
                if (!result.Success)
                {
                    var template = L("Portfolio.Account.HasReferencesError", "尚有 {0} 筆交易引用此帳戶，請先處理");
                    var formatted = string.Format(System.Globalization.CultureInfo.CurrentCulture, template, result.ReferenceCount);
                    _snackbar?.Warning(formatted);
                    return;
                }

                CashAccounts.Remove(row);
                HasNoCashAccounts = CashAccounts.Count == 0;

                if (DefaultCashAccountId == row.Id)
                    await ApplyDefaultCashAccountAsync(null);

                RebuildTotals();
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
        await ApplyDefaultCashAccountAsync(row.Id);
    }

    /// <summary>清除目前的預設現金帳戶。</summary>
    [RelayCommand]
    private async Task ClearDefaultCashAccount()
    {
        await ApplyDefaultCashAccountAsync(null);
    }

    /// <summary>
    /// 切換指定帳戶的預設狀態：已是預設 → 清除；不是 → 設為預設。
    /// 供側面板那顆星形按鈕綁定，一顆按鈕做兩件事。
    /// </summary>
    [RelayCommand]
    private async Task ToggleDefaultCashAccount(CashAccountRowViewModel row)
    {
        if (row is null)
            return;
        await ApplyDefaultCashAccountAsync(row.IsDefault ? null : row.Id);
    }

    /// <summary>
    /// 將 <paramref name="id"/> 寫入 DefaultCashAccountId 並同步更新每一列的 IsDefault
    /// 徽章；同時儲存到 AppSettings。
    /// </summary>
    private async Task ApplyDefaultCashAccountAsync(Guid? id)
    {
        DefaultCashAccountId = id;
        foreach (var r in CashAccounts)
            r.IsDefault = id.HasValue && r.Id == id.Value;

        if (_settingsService is null)
            return;
        try
        {
            var updated = _settingsService.Current with { DefaultCashAccountId = id };
            await _settingsService.SaveAsync(updated);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Portfolio] Failed to persist DefaultCashAccountId");
        }
    }

    /// <summary>取得預設現金帳戶對應的 Row；無設定或帳戶已刪除時回傳 null。</summary>
    public CashAccountRowViewModel? GetDefaultCashAccount() =>
        DefaultCashAccountId is { } id
            ? CashAccounts.FirstOrDefault(r => r.Id == id)
            : null;

    /// <summary>
    /// When true, archived (soft-deleted) accounts are shown in the Cash list.
    /// Plan Task 14 + Task 19 (XAML toggle to be wired later).
    /// </summary>
    [ObservableProperty] private bool _showArchivedAccounts;

    partial void OnShowArchivedAccountsChanged(bool value)
        => _ = LoadCashAccountsAsync();

    private async Task LoadCashAccountsAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyCashAccounts(loaded);
    }

    private void ApplyCashAccounts(PortfolioLoadResult loaded)
    {
        var accounts = loaded.CashAccounts;
        var balances = loaded.CashBalances;
        var visibleAccounts = accounts
            .Where(a => ShowArchivedAccounts || a.IsActive)
            .ToList();
        var newRows = visibleAccounts.ToDictionary(
            a => a.Id,
            a => new CashAccountRowViewModel(a, balances.TryGetValue(a.Id, out var v) ? v : 0m));
        var existingIndex = CashAccounts.ToDictionary(r => r.Id);

        for (var i = CashAccounts.Count - 1; i >= 0; i--)
        {
            if (!newRows.ContainsKey(CashAccounts[i].Id))
                CashAccounts.RemoveAt(i);
        }

        foreach (var account in visibleAccounts)
        {
            var bal = balances.TryGetValue(account.Id, out var v) ? v : 0m;
            if (existingIndex.TryGetValue(account.Id, out var existing))
            {
                existing.Name = account.Name;
                existing.Currency = account.Currency;
                existing.Balance = bal;
                existing.IsActive = account.IsActive;
            }
            else
            {
                CashAccounts.Add(newRows[account.Id]);
            }
        }
        HasNoCashAccounts = CashAccounts.Count == 0;

        var savedId = _settingsService?.Current?.DefaultCashAccountId;
        if (savedId.HasValue && CashAccounts.All(r => r.Id != savedId.Value))
            savedId = null;
        DefaultCashAccountId = savedId;
        foreach (var r in CashAccounts)
            r.IsDefault = savedId.HasValue && r.Id == savedId.Value;

        CashAccountSuggestions.Clear();
        foreach (var a in accounts.Where(a => a.IsActive).OrderBy(a => a.Name))
            CashAccountSuggestions.Add(a.Name);
    }

    private async Task LoadLiabilitiesAsync()
    {
        var loaded = await _loadService.LoadAsync();
        ApplyLiabilities(loaded);
    }

    private void ApplyLiabilities(PortfolioLoadResult loaded)
    {
        var snapshots = loaded.LiabilitySnapshots;
        var loanAssets = loaded.LoanAssets;

        Liabilities.Clear();
        foreach (var (label, snap) in snapshots.OrderBy(kv => kv.Key))
        {
            loanAssets.TryGetValue(label, out var asset);
            Liabilities.Add(new LiabilityRowViewModel(label, snap, asset));
        }

        foreach (var (name, asset) in loanAssets)
        {
            if (!snapshots.ContainsKey(name))
                Liabilities.Add(new LiabilityRowViewModel(name, LiabilitySnapshot.Empty, asset));
        }

        HasNoLiabilities = Liabilities.Count == 0;
        OnPropertyChanged(nameof(LoanLabelSuggestions));
    }

    /// <summary>Loads the amortization schedule for a loan liability row (called on selection).</summary>
    private async Task LoadLoanScheduleAsync(LiabilityRowViewModel row)
    {
        if (!row.IsLoan || row.AssetId is null || _loanScheduleRepo is null)
            return;
        var entries = await _loanScheduleRepo.GetByAssetAsync(row.AssetId.Value).ConfigureAwait(true);
        row.ScheduleEntries.Clear();
        foreach (var e in entries)
            row.ScheduleEntries.Add(new LoanScheduleRowViewModel(e));
        row.IsScheduleLoaded = true;
        row.RefreshScheduleSummary();
    }

    // Account detail side-panel (Cash / Liability) — AscentPortfolio pattern
    //
    // Clicking a row on the Cash or Liability tab opens a right-side panel with
    // tabs (概覽 / 交易記錄), edit & delete controls, and summary stats. This
    // replaces the inline Actions column (edit / delete buttons) that used to
    // live inside the DataGrid — row content is cleaner and the actions have
    // room to breathe.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedCashRow))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTotalDeposits))]
    [NotifyPropertyChangedFor(nameof(SelectedCashTotalWithdrawals))]
    private CashAccountRowViewModel? _selectedCashRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedLiabilityRow))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTotalBorrows))]
    [NotifyPropertyChangedFor(nameof(SelectedLiabilityTotalRepays))]
    private LiabilityRowViewModel? _selectedLiabilityRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRow))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTrades))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionDividendIncome))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionRealizedTotal))]
    [NotifyPropertyChangedFor(nameof(SelectedPositionTradeAvgPrice))]
    [NotifyPropertyChangedFor(nameof(HasSelectedPositionRealized))]
    private PortfolioRowViewModel? _selectedPositionRow;

    /// <summary>
    /// 是否有任何已實現損益資料（賣出價差或股息收入）。用於決定 Detail 面板的
    /// 「已實現損益」區塊是否顯示 — 無資料時隱藏，避免三行 NT$0 佔版面。
    /// </summary>
    public bool HasSelectedPositionRealized =>
        SelectedPositionRealizedTotal != 0m || SelectedPositionDividendIncome > 0m;

    public bool HasSelectedCashRow => SelectedCashRow is not null;
    public bool HasSelectedLiabilityRow => SelectedLiabilityRow is not null;
    public bool HasSelectedPositionRow => SelectedPositionRow is not null;

    /// <summary>
    /// Detail-panel active tab ("overview" or "trades"). Shared between the Cash and
    /// Liability panels — each panel only shows at most one at a time so a single
    /// property is sufficient; resets to "overview" whenever the selected row changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOverviewTab))]
    [NotifyPropertyChangedFor(nameof(IsDetailTradesTab))]
    [NotifyPropertyChangedFor(nameof(IsDetailScheduleTab))]
    private string _detailTab = "overview";

    public bool IsDetailOverviewTab => DetailTab == "overview";
    public bool IsDetailTradesTab => DetailTab == "trades";
    public bool IsDetailScheduleTab => DetailTab == "schedule";

    partial void OnSelectedCashRowChanged(CashAccountRowViewModel? _) => DetailTab = "overview";
    partial void OnSelectedLiabilityRowChanged(LiabilityRowViewModel? row)
    {
        DetailTab = "overview";
        if (row is { IsLoan: true, IsScheduleLoaded: false })
            _ = LoadLoanScheduleAsync(row);
    }
    partial void OnSelectedPositionRowChanged(PortfolioRowViewModel? _) => DetailTab = "overview";

    // Cash account stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedCashTrades =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id)
                    .OrderByDescending(t => t.TradeDate)
            : [];

    public decimal SelectedCashTotalDeposits =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id &&
                                (t.Type == TradeType.Deposit ||
                                 t.Type == TradeType.Income ||
                                 t.Type == TradeType.CashDividend ||
                                 t.Type == TradeType.LoanBorrow ||
                                 t.Type == TradeType.Sell))
                    .Sum(t => Math.Abs(t.CashAmount ?? 0))
            : 0m;

    public decimal SelectedCashTotalWithdrawals =>
        SelectedCashRow is { } r
            ? Trades.Where(t => t.CashAccountId == r.Id &&
                                (t.Type == TradeType.Withdrawal ||
                                 t.Type == TradeType.Buy ||
                                 t.Type == TradeType.LoanRepay))
                    .Sum(t => Math.Abs(t.CashAmount ?? 0))
            : 0m;

    // Liability stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedLiabilityTrades =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => (t.Type == TradeType.LoanBorrow || t.Type == TradeType.LoanRepay) &&
                                t.LoanLabel == r.Label)
                    .OrderByDescending(t => t.TradeDate)
            : [];

    public decimal SelectedLiabilityTotalBorrows =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => t.Type == TradeType.LoanBorrow && t.LoanLabel == r.Label)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    public decimal SelectedLiabilityTotalRepays =>
        SelectedLiabilityRow is { } r
            ? Trades.Where(t => t.Type == TradeType.LoanRepay && t.LoanLabel == r.Label)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    // Investment position stats + filtered trades
    public IEnumerable<TradeRowViewModel> SelectedPositionTrades =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                (t.Type == TradeType.Buy || t.Type == TradeType.Sell ||
                                 t.Type == TradeType.CashDividend || t.Type == TradeType.StockDividend))
                    .OrderByDescending(t => t.TradeDate)
            : [];

    /// <summary>
    /// 成交均價 — 以 Buy 交易紀錄的股數加權平均，**不含買入手續費**。
    /// 對應側面板上「跟交易記錄手動加總」的結果，讓使用者能直接對帳。
    /// 相對地 <see cref="PortfolioRowViewModel.BuyPrice"/> 是含手續費攤平的「成本均價」。
    /// </summary>
    public decimal SelectedPositionTradeAvgPrice
    {
        get
        {
            if (SelectedPositionRow is not { } r)
                return 0m;
            var buys = Trades
                .Where(t => t.IsBuy && string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (buys.Count == 0)
                return 0m;
            var totalQty = buys.Sum(t => (decimal)t.Quantity);
            var totalGross = buys.Sum(t => t.Price * t.Quantity);
            return totalQty > 0 ? totalGross / totalQty : 0m;
        }
    }

    /// <summary>Sum of all CashDividend trades for the selected position (gross dividend income).</summary>
    public decimal SelectedPositionDividendIncome =>
        SelectedPositionRow is { } r
            ? Trades.Where(t => string.Equals(t.Symbol, r.Symbol, StringComparison.OrdinalIgnoreCase) &&
                                t.Type == TradeType.CashDividend)
                    .Sum(t => t.CashAmount ?? 0)
            : 0m;

    /// <summary>
    /// Realized capital gain — proper FIFO/avg-cost lot matching is deferred to a later
    /// pass; for now we report 0 as a placeholder so the realized P&L card layout still
    /// makes sense for users with no sells. (Matches reference design which also shows $0.)
    /// </summary>
    public decimal SelectedPositionCapitalGain => 0m;

    public decimal SelectedPositionRealizedTotal =>
        SelectedPositionDividendIncome + SelectedPositionCapitalGain;

    [RelayCommand]
    private void CloseCashDetail() => SelectedCashRow = null;

    // ── Confirm loan payment ──────────────────────────────────────────────

    [RelayCommand]
    private async Task ConfirmLoanPayment(LoanScheduleRowViewModel? entry)
    {
        if (entry is null || SelectedLiabilityRow is null || _loanScheduleRepo is null)
            return;
        if (entry.IsPaid)
            return;

        var row = SelectedLiabilityRow;
        var cashAccId = TxCashAccount?.Id;
        var result = await _loanPaymentWorkflowService.RecordAsync(new LoanPaymentRequest(
            new LoanScheduleEntry(
                entry.Id,
                row.AssetId ?? Guid.Empty,
                entry.Period,
                entry.DueDate,
                entry.TotalAmount,
                entry.PrincipalAmount,
                entry.InterestAmount,
                entry.Remaining,
                entry.IsPaid,
                entry.PaidAt,
                entry.TradeId),
            row.Label,
            cashAccId,
            DateTime.UtcNow));
        entry.IsPaid = true;
        entry.PaidAt = result.PaidAt;
        entry.TradeId = result.RepayTrade.Id;
        row.RefreshScheduleSummary();

        await LoadTradesAsync();
        await ReloadAccountBalancesAsync();
        RebuildTotals();
    }

    [RelayCommand]
    private void CloseLiabilityDetail() => SelectedLiabilityRow = null;

    [RelayCommand]
    private void ClosePositionDetail() => SelectedPositionRow = null;

    /// <summary>
    /// Permanently removes a position and all its associated Buy / StockDividend trade
    /// records (plus their fee children). Mirrors the cash / liability removal flow.
    /// </summary>
    [RelayCommand]
    private void RemovePosition(PortfolioRowViewModel row)
    {
        if (row is null)
            return;
        var msg = L("Portfolio.Detail.DeleteWarning", "刪除後無法復原，請確認不再需要後再操作。");
        AskConfirm(msg, async () =>
        {
            await _positionDeletionWorkflowService.DeleteAsync(
                new PositionDeletionRequest(row.AllEntryIds.ToList()));

            Positions.Remove(row);
            if (ReferenceEquals(SelectedPositionRow, row))
                SelectedPositionRow = null;
            RebuildTotals();

            await LoadTradesAsync();
            await ReloadAccountBalancesAsync();
        });
    }

    [RelayCommand]
    private void SwitchDetailTab(string tab) => DetailTab = tab;

}
