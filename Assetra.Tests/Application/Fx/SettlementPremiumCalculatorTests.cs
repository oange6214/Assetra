using Assetra.Application.Fx;
using Xunit;

namespace Assetra.Tests.Application.Fx;

/// <summary>
/// 跨幣別「總成本溢價」合理性檢查。核心意圖：複委託的正常雜費不該誤報，
/// 但明顯打錯的金額要被抓出來；而且查不到市場匯率時絕不能擋下記帳。
/// </summary>
public class SettlementPremiumCalculatorTests
{
    // 典型複委託：50 股 × US$31.20 = US$1,560 價金；市場匯率 32.13 → 期望 TWD 50,122.8
    private const decimal Gross = 1_560m;
    private const decimal MarketRate = 32.13m;
    private static decimal Expected => Gross * MarketRate;   // 50,122.8

    [Fact]
    public void Evaluate_TypicalBrokerageFees_IsNormal()
    {
        // 實扣比期望多 ~1.6%（手續費＋匯差）— 複委託的日常，不該被警告。
        var result = SettlementPremiumCalculator.Evaluate(Gross, 50_920m, MarketRate);

        Assert.NotNull(result);
        Assert.Equal(SettlementPremiumGrade.Normal, result!.Value.Grade);
        Assert.InRange(result.Value.Percent, 0.01m, 0.02m);
    }

    [Fact]
    public void Evaluate_ExactlyMarketRate_IsZeroAndNormal()
    {
        var result = SettlementPremiumCalculator.Evaluate(Gross, Expected, MarketRate);

        Assert.NotNull(result);
        Assert.Equal(0m, result!.Value.Percent);
        Assert.Equal(SettlementPremiumGrade.Normal, result.Value.Grade);
    }

    [Theory]
    // 剛好落在 3% 邊界仍算正常（<=）
    [InlineData(1.03, SettlementPremiumGrade.Normal)]
    // 3% 以上、10% 以內 → 警告（不擋）
    [InlineData(1.05, SettlementPremiumGrade.Warning)]
    [InlineData(1.10, SettlementPremiumGrade.Warning)]
    // 超過 10% → 過高（呼叫端要二次確認）
    [InlineData(1.11, SettlementPremiumGrade.Excessive)]
    // 少打一位數：實扣只有 1/10 → 偏差 -90% → 過高
    [InlineData(0.10, SettlementPremiumGrade.Excessive)]
    public void Evaluate_GradesByAbsoluteDeviation(double cashMultiplier, SettlementPremiumGrade expectedGrade)
    {
        var cash = Expected * (decimal)cashMultiplier;

        var result = SettlementPremiumCalculator.Evaluate(Gross, cash, MarketRate);

        Assert.NotNull(result);
        Assert.Equal(expectedGrade, result!.Value.Grade);
    }

    [Fact]
    public void Evaluate_SellDirection_NegativeDeviationGradedByAbsoluteValue()
    {
        // 賣出／股利方向相反：入帳比期望「少」。-1.6% 仍屬正常費用範圍。
        var result = SettlementPremiumCalculator.Evaluate(Gross, Expected * 0.984m, MarketRate);

        Assert.NotNull(result);
        Assert.True(result!.Value.Percent < 0m, "賣出方向偏差應為負");
        Assert.Equal(SettlementPremiumGrade.Normal, result.Value.Grade);
    }

    // 註：InlineData 的值必須是 double 字面值（加 d）；nullable 參數不會做 int→double 的隱式轉換。
    [Theory]
    // 查無市場匯率（fx_rate_history 沒資料）→ 不檢查，絕不擋記帳
    [InlineData(1560d, 50920d, null)]
    // 尚未填實際扣款
    [InlineData(1560d, null, 32.13d)]
    // 尚未填數量/價格
    [InlineData(null, 50920d, 32.13d)]
    // 非正數輸入一律不檢查
    [InlineData(0d, 50920d, 32.13d)]
    [InlineData(1560d, 0d, 32.13d)]
    [InlineData(1560d, 50920d, 0d)]
    public void Evaluate_MissingOrInvalidInputs_ReturnsNullSoRecordingIsNeverBlocked(
        double? gross, double? cash, double? rate)
    {
        var result = SettlementPremiumCalculator.Evaluate(
            (decimal?)gross, (decimal?)cash, (decimal?)rate);

        Assert.Null(result);
    }
}
