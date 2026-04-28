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
    string BenchmarkSymbol = "0050.TW",
    /// <summary>
    /// 估值基準幣別（ISO 4217）。所有跨幣別加總、Performance / Risk / Reports 計算統一換算為此幣別。
    /// 不同於 <see cref="PreferredCurrency"/>（顯示用）：BaseCurrency 是 *valuation* 基準。
    /// </summary>
    string BaseCurrency = "TWD",
    /// <summary>
    /// Tesseract tessdata 目錄絕對路徑（包含 .traineddata 檔）。空字串 = 未設定，PDF 圖片頁不會送 OCR。
    /// </summary>
    string OcrTessdataPath = "",
    /// <summary>Tesseract 語言代碼（例：eng / chi_tra / chi_tra+eng）。預設 eng。</summary>
    string OcrLanguage = "eng",
    // 雲端同步（v0.20.5+）
    /// <summary>啟用雲端同步。false = 同步管線不會建立、Sync UI 顯示停用狀態。</summary>
    bool SyncEnabled = false,
    /// <summary>本裝置 GUID。首次啟用同步時自動產生並持久化；空字串 = 尚未生成。</summary>
    string SyncDeviceId = "",
    /// <summary>雲端後端 base URL（例：<c>https://sync.example.com</c>）。空字串 = 未設定。</summary>
    string SyncBackendUrl = "",
    /// <summary>後端 Bearer token；空字串 = 不送 Authorization header（公開 / 測試後端）。</summary>
    string SyncAuthToken = "",
    /// <summary>
    /// PBKDF2 salt（base64 編碼，至少 16 bytes）。首次設密語時生成並持久化；
    /// 之後同密語才能解出同一把 AES-GCM key。空字串 = 尚未設密語。
    /// </summary>
    string SyncPassphraseSalt = "",
    /// <summary>背景同步間隔（分鐘）。0 = 停用背景同步，僅靠手動觸發。</summary>
    int SyncIntervalMinutes = 0);
