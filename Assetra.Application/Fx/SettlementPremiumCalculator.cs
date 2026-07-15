namespace Assetra.Application.Fx;

/// <summary>溢價分級：正常 / 警告 / 過高。</summary>
public enum SettlementPremiumGrade
{
    /// <summary>偏差在正常費用範圍內（複委託手續費＋匯差）。</summary>
    Normal,

    /// <summary>偏高，值得再看一眼，但不擋下。</summary>
    Warning,

    /// <summary>偏離過大，很可能是打錯字（例如少一位數）。</summary>
    Excessive,
}

/// <param name="Percent">
/// 帶正負號的偏差比例：買入為正代表實際扣款比「價金×市場匯率」多（＝費用＋匯差）；
/// 賣出／股利為負代表實際入帳較少。分級一律取絕對值。
/// </param>
public readonly record struct SettlementPremiumResult(decimal Percent, SettlementPremiumGrade Grade);

/// <summary>
/// 跨幣別交易的「總成本溢價」合理性檢查。
///
/// <para>把使用者填的<b>實際扣款／入帳金額</b>拿去和「價金 × 當日市場匯率」比對，算出偏差比例。
/// 複委託的手續費、交易所費與匯差都隱含在這個偏差裡，所以正常情況本來就會有小幅偏差
/// （通常 &lt;3%）；偏差過大幾乎都是輸入錯誤（少打一位數、填成別的幣別）。</para>
///
/// <para><b>刻意不用「推算匯率 vs 市場匯率」比對</b>：反推匯率會把雜費吸收進去，
/// 對每一筆正常的複委託都會誤報。改用總成本口徑就不會。</para>
///
/// <para>這是純計算、無 I/O，可直接單元測試。查不到市場匯率時呼叫端應直接跳過檢查
/// （回傳 null 亦同）——<b>絕不因為缺匯率資料而擋下記帳</b>。</para>
/// </summary>
public static class SettlementPremiumCalculator
{
    /// <summary>絕對偏差 ≤ 3% 視為正常（複委託常見費用區間）。</summary>
    public const decimal WarningThreshold = 0.03m;

    /// <summary>絕對偏差 &gt; 10% 視為過高，呼叫端應要求二次確認。</summary>
    public const decimal ExcessiveThreshold = 0.10m;

    /// <summary>
    /// 計算溢價。任一輸入缺漏或非正數（含查無市場匯率）即回傳 <c>null</c>＝不做檢查。
    /// </summary>
    /// <param name="grossNative">成交價金，以成交（標的）幣別計，例如 50 股 × US$31.2 = 1560。</param>
    /// <param name="actualCash">實際扣款／入帳金額，以資金帳戶幣別計，例如 TWD 50,920。</param>
    /// <param name="marketRate">交易日市場匯率（1 成交幣別 = N 帳戶幣別），例如 32.13。</param>
    public static SettlementPremiumResult? Evaluate(decimal? grossNative, decimal? actualCash, decimal? marketRate)
    {
        if (grossNative is not { } gross || gross <= 0m)
            return null;
        if (actualCash is not { } cash || cash <= 0m)
            return null;
        if (marketRate is not { } rate || rate <= 0m)
            return null;

        var expected = gross * rate;
        if (expected <= 0m)
            return null;

        var percent = (cash - expected) / expected;
        return new SettlementPremiumResult(percent, Grade(percent));
    }

    /// <summary>依絕對偏差分級（買入偏正、賣出偏負，故取絕對值）。</summary>
    private static SettlementPremiumGrade Grade(decimal percent)
    {
        var abs = Math.Abs(percent);
        if (abs <= WarningThreshold)
            return SettlementPremiumGrade.Normal;
        return abs <= ExcessiveThreshold
            ? SettlementPremiumGrade.Warning
            : SettlementPremiumGrade.Excessive;
    }
}
