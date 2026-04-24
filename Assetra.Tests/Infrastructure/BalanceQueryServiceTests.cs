using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// <see cref="BalanceQueryService"/> 是單一真相的核心：帳戶餘額僅由 Trade 投影而來。
/// 所有測試以 <see cref="FakeTradeRepo"/> 作為 fixture，驗證純函數行為。
/// </summary>
public class BalanceQueryServiceTests
{
    private static Trade Buy(Guid cashId, decimal price, int qty, decimal commission = 0m)
        => new(Guid.NewGuid(), "2330", "TWSE", "TSMC", TradeType.Buy,
               DateTime.Today, price, qty, null, null,
               CashAccountId: cashId, Commission: commission);

    private static Trade Sell(Guid cashId, decimal price, int qty, decimal commission = 0m)
        => new(Guid.NewGuid(), "2330", "TWSE", "TSMC", TradeType.Sell,
               DateTime.Today, price, qty, null, null,
               CashAccountId: cashId, Commission: commission);

    private static Trade Income(Guid cashId, decimal amount)
        => new(Guid.NewGuid(), "", "", "", TradeType.Income,
               DateTime.Today, 0m, 1, null, null,
               CashAmount: amount, CashAccountId: cashId);

    private static Trade Deposit(Guid cashId, decimal amount)
        => new(Guid.NewGuid(), "", "", "", TradeType.Deposit,
               DateTime.Today, 0m, 1, null, null,
               CashAmount: amount, CashAccountId: cashId);

    private static Trade Withdrawal(Guid cashId, decimal amount)
        => new(Guid.NewGuid(), "", "", "", TradeType.Withdrawal,
               DateTime.Today, 0m, 1, null, null,
               CashAmount: amount, CashAccountId: cashId);

    private static Trade Transfer(Guid fromId, Guid toId, decimal amount)
        => new(Guid.NewGuid(), "", "", "", TradeType.Transfer,
               DateTime.Today, 0m, 1, null, null,
               CashAmount: amount, CashAccountId: fromId, ToCashAccountId: toId);

    private static Trade CashDividend(Guid cashId, decimal amount)
        => new(Guid.NewGuid(), "2330", "TWSE", "TSMC", TradeType.CashDividend,
               DateTime.Today, 0m, 0, null, null,
               CashAmount: amount, CashAccountId: cashId);

    private static Trade LoanBorrow(Guid cashId, string loanLabel, decimal amount)
        => new(Guid.NewGuid(), "", "", "", TradeType.LoanBorrow,
               DateTime.Today, 0m, 1, null, null,
               CashAmount: amount, CashAccountId: cashId, LoanLabel: loanLabel);

    private static Trade LoanRepay(Guid cashId, string loanLabel, decimal principal, decimal interest)
        => new(Guid.NewGuid(), "", "", "", TradeType.LoanRepay,
               DateTime.Today, 0m, 1, null, null,
               CashAmount: principal + interest, CashAccountId: cashId,
               LoanLabel: loanLabel,
               Principal: principal, InterestPaid: interest);

    private static Trade CreditCardCharge(Guid cardId, string cardName, decimal amount)
        => new(Guid.NewGuid(), "", "", cardName, TradeType.CreditCardCharge,
               DateTime.Today, 0m, 1, null, null,
               CashAmount: amount, LiabilityAssetId: cardId);

    private static Trade CreditCardPayment(Guid cardId, string cardName, Guid cashId, decimal amount)
        => new(Guid.NewGuid(), "", "", cardName, TradeType.CreditCardPayment,
               DateTime.Today, 0m, 1, null, null,
               CashAmount: amount, CashAccountId: cashId, LiabilityAssetId: cardId);

    private static BalanceQueryService Create(params Trade[] trades)
    {
        var repo = new FakeTradeRepo();
        repo.Store.AddRange(trades);
        return new BalanceQueryService(repo);
    }

    // ─── Cash balance: basic credits/debits ──────────────────────────────

    [Fact]
    public async Task Cash_EmptyHistory_ReturnsZero()
    {
        var svc = Create();
        var bal = await svc.GetCashBalanceAsync(Guid.NewGuid());
        Assert.Equal(0m, bal);
    }

    [Fact]
    public async Task Cash_DepositsAndWithdrawals_NetsCorrectly()
    {
        var id = Guid.NewGuid();
        var svc = Create(Deposit(id, 100_000m), Withdrawal(id, 30_000m), Deposit(id, 5_000m));
        Assert.Equal(75_000m, await svc.GetCashBalanceAsync(id));
    }

    [Fact]
    public async Task Cash_Income_Credits()
    {
        var id = Guid.NewGuid();
        var svc = Create(Income(id, 60_000m), Income(id, 12_000m));
        Assert.Equal(72_000m, await svc.GetCashBalanceAsync(id));
    }

    [Fact]
    public async Task Cash_CashDividend_Credits()
    {
        var id = Guid.NewGuid();
        var svc = Create(CashDividend(id, 2_500m));
        Assert.Equal(2_500m, await svc.GetCashBalanceAsync(id));
    }

    [Fact]
    public async Task Cash_BuyDebitsIncludesCommission()
    {
        var id = Guid.NewGuid();
        var svc = Create(Deposit(id, 1_000_000m), Buy(id, 500m, 1000, commission: 713m));
        // 1_000_000 − (500×1000 + 713) = 499_287
        Assert.Equal(499_287m, await svc.GetCashBalanceAsync(id));
    }

    [Fact]
    public async Task Cash_SellCreditsNetOfCommission()
    {
        var id = Guid.NewGuid();
        var svc = Create(Sell(id, 600m, 1000, commission: 855m));
        // +(600×1000 − 855) = 599_145
        Assert.Equal(599_145m, await svc.GetCashBalanceAsync(id));
    }

    // ─── Cash balance: transfers ─────────────────────────────────────────

    [Fact]
    public async Task Cash_Transfer_DebitsSourceAndCreditsTarget()
    {
        var src = Guid.NewGuid();
        var dst = Guid.NewGuid();
        var svc = Create(
            Deposit(src, 100_000m),
            Transfer(src, dst, 40_000m));
        Assert.Equal(60_000m, await svc.GetCashBalanceAsync(src));
        Assert.Equal(40_000m, await svc.GetCashBalanceAsync(dst));
    }

    [Fact]
    public async Task Cash_UnrelatedAccount_NotAffected()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var svc = Create(Deposit(a, 100_000m), Withdrawal(a, 20_000m));
        Assert.Equal(0m, await svc.GetCashBalanceAsync(b));
    }

    // ─── Liability projection ────────────────────────────────────────────

    [Fact]
    public async Task Liability_EmptyHistory_ReturnsEmpty()
    {
        var svc = Create();
        var snap = await svc.GetLiabilitySnapshotAsync("不存在");
        Assert.Equal(LiabilitySnapshot.Empty, snap);
    }

    [Fact]
    public async Task Liability_LoanBorrow_RaisesBalanceAndOriginal()
    {
        var cash = Guid.NewGuid();
        var svc = Create(LoanBorrow(cash, "台新信貸", 2_000_000m));
        var snap = await svc.GetLiabilitySnapshotAsync("台新信貸");
        Assert.Equal(2_000_000m, snap.Balance);
        Assert.Equal(2_000_000m, snap.OriginalAmount);
    }

    [Fact]
    public async Task Liability_LoanRepayPrincipal_ReducesBalanceOnly()
    {
        var cash = Guid.NewGuid();
        var svc = Create(
            LoanBorrow(cash, "台新信貸", 2_000_000m),
            LoanRepay(cash, "台新信貸", principal: 50_000m, interest: 5_000m));
        var snap = await svc.GetLiabilitySnapshotAsync("台新信貸");
        Assert.Equal(1_950_000m, snap.Balance);
        Assert.Equal(2_000_000m, snap.OriginalAmount); // interest never affects original
    }

    [Fact]
    public async Task Liability_MultipleBorrowsAndRepays_AggregateCorrectly()
    {
        var cash = Guid.NewGuid();
        var svc = Create(
            LoanBorrow(cash, "台新信貸", 1_000_000m),
            LoanBorrow(cash, "台新信貸", 500_000m),
            LoanRepay(cash, "台新信貸", principal: 100_000m, interest: 10_000m),
            LoanRepay(cash, "台新信貸", principal: 200_000m, interest: 8_000m));
        var snap = await svc.GetLiabilitySnapshotAsync("台新信貸");
        Assert.Equal(1_200_000m, snap.Balance);         // 1.5M − 300k principal
        Assert.Equal(1_500_000m, snap.OriginalAmount);  // 2× borrow
    }

    [Fact]
    public async Task Liability_RepayWithNullPrincipal_TreatsFullCashAmountAsPrincipal_LegacyCompat()
    {
        var cash = Guid.NewGuid();
        // Legacy record (no Principal split) — entire CashAmount counts as principal
        var legacyRepay = new Trade(Guid.NewGuid(), "", "", "", TradeType.LoanRepay,
            DateTime.Today, 0m, 1, null, null,
            CashAmount: 30_000m, CashAccountId: cash, LoanLabel: "台新信貸",
            Principal: null, InterestPaid: null);
        var svc = Create(LoanBorrow(cash, "台新信貸", 100_000m), legacyRepay);
        var snap = await svc.GetLiabilitySnapshotAsync("台新信貸");
        Assert.Equal(70_000m, snap.Balance);
    }

    // ─── Cash/Liability cross effects ────────────────────────────────────

    [Fact]
    public async Task Cash_LoanBorrowCredits_LoanRepayDebits()
    {
        var cash = Guid.NewGuid();
        var svc = Create(
            LoanBorrow(cash, "台新信貸", 2_000_000m),
            LoanRepay(cash, "台新信貸", principal: 50_000m, interest: 5_000m));
        // +2_000_000 − (50_000+5_000) = 1_945_000
        Assert.Equal(1_945_000m, await svc.GetCashBalanceAsync(cash));
    }

    [Fact]
    public async Task Cash_CreditCardPayment_DebitsCashAccount()
    {
        var cash = Guid.NewGuid();
        var card = Guid.NewGuid();
        var svc = Create(
            Deposit(cash, 80_000m),
            CreditCardPayment(card, "玉山 Pi 卡", cash, 12_345m));

        Assert.Equal(67_655m, await svc.GetCashBalanceAsync(cash));
    }

    // ─── Bulk queries ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllCashBalances_ReturnsEveryTouchedAccount()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var svc = Create(
            Deposit(a, 100_000m),
            Income(b, 50_000m),
            Transfer(a, c, 20_000m));
        var all = await svc.GetAllCashBalancesAsync();
        Assert.Equal(80_000m, all[a]);
        Assert.Equal(50_000m, all[b]);
        Assert.Equal(20_000m, all[c]);
    }

    [Fact]
    public async Task GetAllLiabilitySnapshots_ReturnsEveryTouchedAccount()
    {
        var cash = Guid.NewGuid();
        var svc = Create(
            LoanBorrow(cash, "台新信貸", 1_000_000m),
            LoanRepay(cash, "台新信貸", principal: 50_000m, interest: 3_000m),
            LoanBorrow(cash, "玉山信貸", 300_000m));
        var all = await svc.GetAllLiabilitySnapshotsAsync();
        Assert.Equal(new LiabilitySnapshot(950_000m, 1_000_000m), all["台新信貸"]);
        Assert.Equal(new LiabilitySnapshot(300_000m,   300_000m), all["玉山信貸"]);
    }

    [Fact]
    public async Task Liability_CreditCardChargeAndPayment_ProjectOutstandingBalance()
    {
        var cash = Guid.NewGuid();
        var card = Guid.NewGuid();
        var svc = Create(
            CreditCardCharge(card, "玉山 Pi 卡", 8_000m),
            CreditCardCharge(card, "玉山 Pi 卡", 2_000m),
            CreditCardPayment(card, "玉山 Pi 卡", cash, 3_500m));

        var snap = await svc.GetLiabilitySnapshotAsync("玉山 Pi 卡");
        Assert.Equal(6_500m, snap.Balance);
        Assert.Equal(10_000m, snap.OriginalAmount);
    }

    [Fact]
    public async Task GetAllLiabilitySnapshots_IncludesCreditCards()
    {
        var cash = Guid.NewGuid();
        var card = Guid.NewGuid();
        var svc = Create(
            LoanBorrow(cash, "台新信貸", 500_000m),
            CreditCardCharge(card, "富邦 J 卡", 20_000m),
            CreditCardPayment(card, "富邦 J 卡", cash, 4_000m));

        var all = await svc.GetAllLiabilitySnapshotsAsync();
        Assert.Equal(new LiabilitySnapshot(500_000m, 500_000m), all["台新信貸"]);
        Assert.Equal(new LiabilitySnapshot(16_000m, 20_000m), all["富邦 J 卡"]);
    }

    // ─── Fake repo ───────────────────────────────────────────────────────

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid id) =>
            Task.FromResult<IReadOnlyList<Trade>>(
                Store.Where(t => t.CashAccountId == id || t.ToCashAccountId == id).ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel) =>
            Task.FromResult<IReadOnlyList<Trade>>(
                Store.Where(t => t.LoanLabel == loanLabel).ToList());
        public Task AddAsync(Trade t) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t)
        {
            var i = Store.FindIndex(x => x.Id == t.Id);
            if (i >= 0) Store[i] = t;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(Guid id) { Store.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
        public Task RemoveChildrenAsync(Guid parentId) { Store.RemoveAll(x => x.ParentTradeId == parentId); return Task.CompletedTask; }
        public Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default) { Store.RemoveAll(x => x.CashAccountId == accountId || x.ToCashAccountId == accountId); return Task.CompletedTask; }
    }
}
