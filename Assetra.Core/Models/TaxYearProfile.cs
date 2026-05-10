namespace Assetra.Core.Models;

/// <summary>
/// 單一綜所稅級距（progressive bracket）。
/// 應納稅額 = 課稅所得淨額 × Rate − Subtract（速算公式）。
/// 區間判定規則：UpTo 為「上限（含）」；最頂級距 UpTo = null 表示無上限。
/// </summary>
/// <param name="UpTo">本級距課稅所得上限（NTD）。null = 最高級距無上限。</param>
/// <param name="Rate">本級距邊際稅率（小數，例：0.05 表 5%）。</param>
/// <param name="Subtract">速算扣除額（NTD），用於 progressive 計算。</param>
public sealed record TaxBracket(decimal? UpTo, decimal Rate, decimal Subtract);

/// <summary>
/// 一個年度的台灣個人綜合所得稅 + 最低稅負制（AMT）官方參數快照。
/// <para>
/// 來源：財政部年度公告（級距、免稅額、扣除額均依物價指數 CPI 連動）。
/// 內建於 <c>Assetra.Core/Resources/TaxYearProfiles.json</c>，由
/// <c>EmbeddedTaxProfileProvider</c> 載入；缺漏的年度由 Provider fallback 至
/// 最接近的已知年度，並在 <see cref="IsExtrapolated"/> 標示。
/// </para>
/// </summary>
public sealed record TaxYearProfile(
    int Year,

    // ── 綜所稅級距 ────────────────────────────────────────────────────
    /// <summary>累進級距清單（依 UpTo 由小到大排序）。</summary>
    IReadOnlyList<TaxBracket> IncomeTaxBrackets,

    // ── 免稅額與扣除額 ──────────────────────────────────────────────
    /// <summary>每人免稅額（NTD）。70 歲以上加成另計。</summary>
    decimal PersonalExemption,

    /// <summary>單身標準扣除額（NTD）。</summary>
    decimal StandardDeductionSingle,

    /// <summary>夫妻合併標準扣除額（NTD），= 單身的 2 倍（粗略）。</summary>
    decimal StandardDeductionMarried,

    /// <summary>薪資特別扣除額（NTD），上限制。</summary>
    decimal SalarySpecialDeduction,

    /// <summary>儲蓄投資特別扣除額上限（NTD），現行 27 萬。</summary>
    decimal SavingsInvestmentDeductionCap,

    /// <summary>長期照顧特別扣除額（NTD / 每位被照顧者）。</summary>
    decimal LongCareDeduction,

    /// <summary>幼兒學前特別扣除額（NTD / 每位 6 歲以下子女）。</summary>
    decimal PreschoolDeduction,

    /// <summary>身心障礙特別扣除額（NTD / 每位）。</summary>
    decimal DisabilityDeduction,

    /// <summary>教育學費特別扣除額上限（NTD / 每位大專子女）。</summary>
    decimal EducationDeduction,

    /// <summary>房屋租金支出特別扣除額上限（NTD，2024 起列特別扣除）。</summary>
    decimal RentalDeduction,

    // ── 股利課稅 ────────────────────────────────────────────────────
    /// <summary>股利合併計稅：抵減比率（小數，現行 0.085）。</summary>
    decimal DividendCreditRate,

    /// <summary>股利合併計稅：抵減上限（NTD / 每戶，現行 8 萬）。</summary>
    decimal DividendCreditCap,

    /// <summary>股利分離課稅稅率（小數，現行 0.28）。</summary>
    decimal DividendSeparateRate,

    // ── 最低稅負制（AMT）參數 ──────────────────────────────────────
    /// <summary>AMT 基本所得免稅額（NTD）。2014–2023 = 670 萬；2024 起 = 750 萬。</summary>
    decimal AmtExemption,

    /// <summary>AMT 稅率（小數，現行 0.20）。</summary>
    decimal AmtRate,

    /// <summary>海外所得納入 AMT 的申報門檻（NTD），現行 100 萬。</summary>
    decimal AmtOverseasThreshold,

    /// <summary>保險死亡給付不計入基本所得的扣除額（NTD），2024 起 = 3,740 萬。</summary>
    decimal AmtInsuranceDeduction,

    // ── Meta ────────────────────────────────────────────────────────
    /// <summary>true = 此 profile 為 fallback 推估（找不到目標年度時取最接近年）。</summary>
    bool IsExtrapolated = false);
