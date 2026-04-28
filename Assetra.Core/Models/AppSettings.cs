namespace Assetra.Core.Models;

public sealed record AppSettings(
    // 歷史資料來源
    string HistoryProvider = "twse",
    string QuoteProvider = "official",
    string FugleApiKey = "",
    int RefreshIntervalSeconds = 10,
    bool TaiwanColorScheme = true,   // true = 漲紅跌綠（台灣慣例，預設）
    // 貨幣
    string PreferredCurrency = "TWD",
    decimal UsdTwdRate = 32.0m,      // 1 USD = N TWD（向後相容；同步至 ExchangeRates["USD"]）
    /// <summary>
    /// 各幣別兌台幣匯率（1 單位外幣 = N 台幣）。
    /// null 表示尚未從 Frankfurter 取得，此時 CurrencyService 使用硬碼預設值。
    /// </summary>
    Dictionary<string, decimal>? ExchangeRates = null,
    // 語言
    string Language = "zh-TW",    // zh-TW | en-US
    // 介面縮放
    double UiScale = 1.0,   // 0.9 = 緊湊 / 1.0 = 正常 / 1.15 = 舒適
    // 通知
    bool AlertNotifications = true,   // 警示觸發時顯示系統通知
    // 資產配置目標
    /// <summary>每個資產的目標比例（0–100）。Key = Symbol（或現金帳戶名稱）。</summary>
    Dictionary<string, decimal>? TargetAllocations = null,
    // 現金管理
    /// <summary>每月預估支出（台幣），用於計算緊急預備金可用月數。預設 0 表示未設定。</summary>
    decimal MonthlyExpense = 0m,
    /// <summary>
    /// 預設現金帳戶 Id — 新增交易時自動帶入的帳戶。null 表示未設定。
    /// 被設為預設的帳戶若遭刪除，本欄位會自動清為 null。
    /// </summary>
    Guid? DefaultCashAccountId = null,
    /// <summary>目標淨資產（台幣），用於儀表板目標進度列。預設 0 表示未設定。</summary>
    decimal GoalNetWorth = 0m,
    /// <summary>首次啟動歡迎橫幅已關閉。false = 顯示，true = 已忽略。</summary>
    bool HasShownWelcomeBanner = false,
    /// <summary>績效報表使用的市場基準代號（例：0050.TW）。空字串表示停用 benchmark 比較。</summary>
    string BenchmarkSymbol = "0050.TW");
