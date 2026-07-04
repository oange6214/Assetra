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
    /// null 表示尚未從台灣銀行取得，此時 CurrencyService 使用硬碼預設值。
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
    int SyncIntervalMinutes = 0,
    /// <summary>
    /// 財務總覽 KPI 卡片選擇 — 以逗號分隔的 KpiMetric 列舉名稱字串
    /// （例：<c>"NetWorth,Investments,InvestmentPnl,DebtRatio"</c>）。
    /// 空字串 = 使用預設組合（Plan B 推薦：淨資產 / 投資組合 / 投資淨損益 / 負債比率）。
    /// 未知名稱在載入時靜默丟棄。
    /// </summary>
    string OverviewKpis = "",
    /// <summary>
    /// 預設啟動分頁 — NavSection 列舉名稱字串（例：<c>"FinancialOverview"</c>、
    /// <c>"Portfolio"</c>）。空字串 / 未知名稱 = fallback 至 FinancialOverview。
    /// </summary>
    string DefaultHomeSection = "FinancialOverview",

    // ── 最低稅負制（AMT）參數 ─────────────────────────────────────────
    // v2：免稅額 / 稅率 / 海外門檻 / 保險扣除額由 TaxYearProfile 依年度提供，
    // 不再需要使用者手動填寫。以下欄位為「使用者年度報稅彙整輸入」。

    /// <summary>使用者填寫的一般綜合所得淨額（NTD）。0 = 未填，AMT 不計算。</summary>
    decimal AmtRegularTaxableIncome = 0m,

    /// <summary>使用者填寫的一般綜合所得稅應納稅額（NTD）。0 = 未填則由 IncomeTaxCalculator 自動估算。</summary>
    decimal AmtRegularIncomeTax = 0m,

    /// <summary>
    /// 受益人與要保人不同 + 人壽/年金保險「死亡給付」總額（NTD）。
    /// 超過該年度 <c>AmtInsuranceDeduction</c>（2024 = 3,740 萬）部分計入 AMT。
    /// 健康/傷害保險、要保人=受益人之給付都不計入此欄。
    /// </summary>
    decimal AmtInsuranceDeathProceeds = 0m,

    /// <summary>
    /// 受益人與要保人不同 + 人壽/年金保險「非死亡給付」（滿期 / 解約）總額（NTD）。
    /// 全數計入 AMT，無扣除額。健康/傷害保險不計入此欄。
    /// </summary>
    decimal AmtInsuranceNonDeathProceeds = 0m,

    /// <summary>未上市櫃股票交易所得（NTD）— 計入 AMT。</summary>
    decimal AmtUnlistedSecurityGains = 0m,

    /// <summary>非現金（藝術品、不動產等）捐贈扣除額（NTD）— 加計回基本所得。</summary>
    decimal AmtNonCashDonation = 0m,

    /// <summary>私募證券投資信託基金受益憑證交易所得（NTD）— 計入 AMT。</summary>
    decimal AmtPrivateFundGains = 0m,

    /// <summary>海外已納稅額可扣抵 AMT（NTD）。</summary>
    decimal AmtOverseasTaxCredit = 0m,

    // ── 個人稅務檔案（用於綜所稅自動試算）────────────────────────────
    /// <summary>是否已婚合併申報（true = 夫妻合併、適用 2× 標準扣除額）。</summary>
    bool TaxIsMarried = false,

    /// <summary>扶養親屬人數（不含本人配偶）。每人加計一份免稅額。</summary>
    int TaxDependentCount = 0,

    /// <summary>6 歲以下幼兒人數，享幼兒學前特別扣除額。</summary>
    int TaxPreschoolCount = 0,

    /// <summary>大專以上就學子女人數，享教育學費特別扣除額。</summary>
    int TaxCollegeStudentCount = 0,

    /// <summary>長期照顧需求人數（直系親屬），享長照特別扣除額。</summary>
    int TaxLongCareCount = 0,

    /// <summary>身心障礙人數，享身心障礙特別扣除額。</summary>
    int TaxDisabilityCount = 0,

    /// <summary>本人薪資所得（NTD / 年）— 用於估算一般綜所稅。0 = 未填。</summary>
    decimal TaxSalaryIncome = 0m,

    /// <summary>銀行存款利息所得（NTD）— 享儲蓄投資特別扣除額。</summary>
    decimal TaxInterestIncome = 0m,

    /// <summary>房屋租金支出（NTD / 年）— 2024 起列特別扣除額。</summary>
    decimal TaxRentalExpense = 0m,

    /// <summary>true = 採列舉扣除；false = 採標準扣除。Default false。</summary>
    bool TaxUseItemizedDeduction = false,

    /// <summary>列舉扣除總額（NTD）— 僅 TaxUseItemizedDeduction = true 時使用。</summary>
    decimal TaxItemizedDeductionAmount = 0m,

    /// <summary>股利課稅選擇：true = 28% 分離課稅；false = 合併計稅（享 8.5% 抵減上限 8 萬）。</summary>
    bool TaxDividendSeparate = false,

    /// <summary>
    /// 最近一次台灣銀行匯率刷新成功時間（UTC ISO-8601）。
    /// 空字串 = 從未刷新成功，UI 顯示「尚未更新」。
    /// </summary>
    string LastFxRefreshUtc = "",

    // AI Phase 3 — LLM provider settings.

    /// <summary>"" / "null" = rule-based only; "openai"; "ollama".</summary>
    string LlmProvider = "",

    /// <summary>API key for cloud LLM providers (OpenAI). Stored plaintext (legacy);
    /// future work moves to OS credential store. Empty = provider runs unconfigured.</summary>
    string LlmApiKey = "",

    /// <summary>Model identifier override. Empty = provider default
    /// ("gpt-4o-mini" for OpenAI, "llama3.1:8b" for Ollama).</summary>
    string LlmModel = "",

    /// <summary>Endpoint override (Ollama only — defaults to http://localhost:11434).</summary>
    string LlmEndpoint = "",

    // US market data — Twelve Data Basic provider.

    /// <summary>Twelve Data API key. Stored plaintext for now; future work moves to OS credential store.</summary>
    string TwelveDataApiKey = "",

    /// <summary>Date key for daily Twelve Data quota display, formatted as yyyy-MM-dd in UTC.</summary>
    string TwelveDataQuotaDate = "",

    /// <summary>Credits used for the date stored in <see cref="TwelveDataQuotaDate"/>.</summary>
    int TwelveDataQuotaUsed = 0,

    /// <summary>Soft daily quota guard for Twelve Data Basic. UI shows used / limit.</summary>
    int TwelveDataDailyQuota = 800,

    /// <summary>
    /// 已關閉的 Assistant insight 鍵 → dismiss 時間。key = $"{Source}|{Title}"。
    /// 用於儀表板總覽 widget 與 Assistant 頁的「✕」一致地隱藏使用者已忽略
    /// 的提示，避免每次重啟又跳出來。7 天後自動 expire（在 service 端過濾時
    /// 檢查 timestamp）。
    /// </summary>
    Dictionary<string, DateTime>? DismissedAssistantInsights = null,

    /// <summary>
    /// v2：資產類焦點卡 6 個 cell 的顯示偏好。null 或缺鍵 = 顯示。
    /// Key 取 AssetClassFocusKey enum 字串名（Cash / Liability / RealEstate /
    /// Insurance / Retirement / Physical）。
    /// </summary>
    Dictionary<string, bool>? AssetClassFocusVisibility = null,

    /// <summary>
    /// [Deprecated] 舊「自訂對標」symbol 清單。對標子系統已移除（比較改用 <see cref="ComparisonItems"/> chips）；
    /// 此欄位僅保留以相容舊 settings.json（反序列化不報錯），新版不讀寫、儲存時 with 沿用原值。
    /// </summary>
    List<string>? CustomBenchmarkSymbols = null,

    /// <summary>
    /// 資產趨勢「比較」圖的項目清單（使用者自選、可空、最多 6）。token：「@me」＝我的投組（整體 TWR）、
    /// 其餘為 symbol（如 ^TWII 大盤、0050.TW、3231.TW）。與 CustomBenchmarkSymbols（舊對標數字）獨立。
    /// </summary>
    List<string>? ComparisonItems = null,

    /// <summary>
    /// MultiCurrency-Reporting P4.1d — 上次成功跑歷史匯率 backfill 的 UTC 時間戳。
    /// null = 尚未跑過（新使用者 / 安裝後第一次 startup 還沒到 5-sec delay）。
    /// 設定頁顯示給使用者看 + 提供「立即更新」按鈕。
    /// 不同於 <see cref="LastFxRefreshUtc"/>（live FX rate 即時報價）— 這個是
    /// historical FX backfill 跑進 <c>fx_rate_history</c> 表的時間戳。
    /// </summary>
    DateTimeOffset? LastFxHistoryRefreshAt = null,

    /// <summary>
    /// 預設手續費折扣 (0.1 ~ 1.0；1.0 = 無折扣 / 0.6 = 六折)。新增買入交易 dialog 開啟時
    /// 自動帶入此值，使用者鮮少改券商折扣，所以從 dialog 內移到設定一次定終身。
    /// 使用者在 dialog 內手動覆寫的「手續費（選填）」欄位仍是 trade-level override，
    /// 不受此預設影響。
    /// </summary>
    decimal DefaultCommissionDiscount = 1.0m,

    /// <summary>
    /// 新增交易 dialog「最近使用的資產」分組來源。最新（最後使用的）排在 [0]、最舊在末尾。
    /// 上限由 <see cref="MaxRecentlyUsedAssets"/> 控制；超過時砍尾。
    /// <para>
    /// Id 對應 PortfolioEntry.Id / CashAccount.Id / Liability.AssetId。被刪除的資產 id
    /// 仍可能留在這個清單裡 — 讀的時候 join 到 AvailableAssets 找不到就跳過。
    /// </para>
    /// null = 還沒紀錄過任何資產（新使用者）；空 list = 紀錄過但都被清掉。
    /// </summary>
    List<System.Guid>? RecentlyUsedAssetIds = null,

    /// <summary>
    /// 投資資產頁上方「概覽」（走勢圖＋投資組合焦點＋insights chips）的展開/收合偏好。
    /// 預設 true（展開，首次使用展示組合輪廓）；使用者切換後持久化，下次啟動還原。
    /// 收合時頭條（市值／損益／今日漲跌）仍顯示，只收走勢圖與焦點卡 / insights。
    /// </summary>
    bool PortfolioOverviewExpanded = true,

    /// <summary>投資資產頁「顯示已平倉」偏好。預設 false（隱藏賣光部位）；使用者切換後持久化、下次啟動還原。</summary>
    bool PortfolioShowClosed = false,

    /// <summary>
    /// 資產趨勢／詳情走勢圖的期間選擇（chip key："5"/"30"/"180"/"YTD"/"365"/"1825"/"All"）。
    /// 空字串 = 用預設 "30"。使用者切換後持久化、下次啟動還原。
    /// </summary>
    string PortfolioHistoryPeriod = "",

    /// <summary>月結報告上次檢視的年（西元）。0 = 用當年。使用者切換月份後持久化、下次啟動還原。</summary>
    int ReportsYear = 0,

    /// <summary>月結報告上次檢視的月（1–12）。0 = 用當月。</summary>
    int ReportsMonth = 0,

    /// <summary>報酬日曆色階基準：true = 按絕對值、false = 按 %（預設）。使用者切換後持久化。</summary>
    bool CalendarUseAbsoluteTone = false,

    /// <summary>報酬日曆檢視模式：true = 年度熱度圖、false = 月格（預設）。</summary>
    bool CalendarYearView = false,

    /// <summary>FIRE 路徑分頁（FirePathTab enum 名："Wealth" / "Drawdown"）。空字串 = Wealth。</summary>
    string FirePathTab = "",

    /// <summary>
    /// 導覽列各群組的展開偏好 — 以逗號分隔「展開中」群組的 <c>TitleResourceKey</c>
    /// （例：<c>"Nav.Analysis,Nav.Assets"</c>）。空字串 = 沿用 BuildGroups() 的程式碼預設
    /// （核心群展開、進階群收合）。使用者手動展開／收合後持久化，下次啟動還原。
    /// </summary>
    string NavExpandedGroups = "")
{
    /// <summary>「最近使用的資產」分組顯示上限。超過時 LRU 砍尾。</summary>
    public const int MaxRecentlyUsedAssets = 6;
}
