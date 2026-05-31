using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.SubViewModels.Tx;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// 轉帳「轉出 / 轉入金額」連動行為。政策：來源與目標同幣別時轉入金額自動鏡射轉出金額
/// （省去重複輸入），但欄位仍可手動改；跨幣別時不自動帶入（實收金額由匯率決定，需使用者填）。
/// </summary>
public sealed class TransferTxViewModelTests
{
    private static CashAccountRowViewModel Account(string currency) =>
        new(new AssetItem(
            Guid.NewGuid(), $"{currency} Acc", FinancialType.Asset, null, currency,
            DateOnly.FromDateTime(DateTime.Today)), 0m);

    [Fact]
    public void SameCurrency_TypingSourceAmount_AutoFillsTargetAmount()
    {
        // WHY: 已選同幣別目標帳戶時「轉出 == 轉入」，不該強迫使用者把同一個金額打兩次。
        var vm = new TransferTxViewModel { Target = Account("TWD") };  // 來源預設 TWD、目標 TWD → 同幣別

        vm.SourceAmountText = "200000";

        Assert.Equal("200000", vm.TargetAmount);
    }

    [Fact]
    public void DifferentCurrency_TypingSourceAmount_DoesNotAutoFillTarget()
    {
        // WHY: 跨幣別時實收金額由匯率決定，自動帶入轉出金額會給出錯誤數字，必須留白讓使用者填。
        var vm = new TransferTxViewModel { SourceCurrency = "USD", Target = Account("TWD") };  // USD → TWD 跨幣別

        vm.SourceAmountText = "200000";

        Assert.Equal(string.Empty, vm.TargetAmount);
    }

    [Fact]
    public void ManuallyEditedTarget_IsNotOverwrittenByLaterSourceChange()
    {
        // WHY: 「欄位仍可手動改」的核心——使用者改過轉入金額後，再調整轉出金額不可覆蓋他的輸入。
        var vm = new TransferTxViewModel { Target = Account("TWD") };
        vm.SourceAmountText = "200000";        // 自動帶入 → 200000
        Assert.Equal("200000", vm.TargetAmount);

        vm.TargetAmount = "180000";            // 使用者手動覆寫
        vm.SourceAmountText = "250000";        // 之後再改轉出

        Assert.Equal("180000", vm.TargetAmount);  // 手動值必須保留
    }

    [Fact]
    public void SelectingSameCurrencyTarget_AfterTypingSource_SyncsTargetAmount()
    {
        // WHY: 輸入順序不該影響結果——先打金額、後選同幣別目標帳戶時也要帶入。
        var vm = new TransferTxViewModel { SourceCurrency = "USD" };
        vm.SourceAmountText = "200000";        // 此時 Target 為 null(TWD) ≠ USD → 不帶入
        Assert.Equal(string.Empty, vm.TargetAmount);

        vm.Target = Account("USD");            // 選了同幣別 USD 目標帳戶

        Assert.Equal("200000", vm.TargetAmount);
    }

    [Fact]
    public void TargetCurrency_ReflectsSelectedTargetAccount()
    {
        // WHY: 使用者要看得到來源／目的的幣別，才理解兩個金額為何可能不同。
        var vm = new TransferTxViewModel();
        Assert.Equal("TWD", vm.TargetCurrency);   // 未選帳戶 → 預設 TWD

        vm.Target = Account("USD");

        Assert.Equal("USD", vm.TargetCurrency);
    }

    [Fact]
    public void Reset_ClearsAmountsAndRestoresDefaults()
    {
        // WHY: 開新一筆轉帳時不可殘留前一筆的金額與幣別。
        var vm = new TransferTxViewModel { SourceCurrency = "USD" };
        vm.SourceAmountText = "200000";
        vm.TargetAmount = "1000";

        vm.Reset();

        Assert.Equal(string.Empty, vm.SourceAmountText);
        Assert.Equal(string.Empty, vm.TargetAmount);
        Assert.Equal("TWD", vm.SourceCurrency);
    }
}
