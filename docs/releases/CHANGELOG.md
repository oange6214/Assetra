# Changelog

## v0.6.0 - 2026-04-26

收尾 v0.6.0 sprint：月結報告 UI、淨資產趨勢視覺化，以及 Goals MVP。

### 重點

- **月結報告 UI（F1）** — 新增 Reports 頁面，含月份選擇器、四張指標卡（收入 / 支出 / 淨額 / 儲蓄率，皆附與上月差額）、超支清單、近期到期清單，後端由 `MonthEndReportService` 提供。
- **淨資產趨勢（F2）** — 新增 Trends 頁面，沿用 `PortfolioHistoryViewModel`。提供 30 / 90 / 180 / 365 / All 預設區間按鈕，加上 `DateRangePicker` 自訂範圍（兩端皆設定時覆蓋預設）。
- **Goals MVP（F3）** — 全新 bounded context：`FinancialGoal` 模型、`IFinancialGoalRepository` + `GoalSqliteRepository`、`GoalSchemaMigrator`，並新增 Goals 頁面含進度條、期限顯示與新增表單（FormTextBox + FormDatePicker + 欄位標籤）。
- **SetupNotice 切片** — 從 `PortfolioViewModel` 抽出 `SetupNoticeViewModel`（顯示 / 標題 / 訊息 / 動作文字 + 執行命令），延續 v0.6.0 的 D1-2 SubViewModels 重構工作。
- **主題 / 語言 / 字級稽核** — 三個新頁面：`AppText` → `AppTextPrimary`、`FontSize="{StaticResource …}"` → `{DynamicResource …}`，確保執行期主題與字級切換能即時生效。

### 內部變更

- 新增 `Assetra.WPF/Features/Reports/`、`Assetra.WPF/Features/Trends/`、`Assetra.WPF/Features/Goals/` 功能資料夾，並在 Nav rail 與 `MainViewModel` / `MainWindow` 接好內容區塊。
- 在 `AppBootstrapper` 註冊 `AddGoalsContext` DI 擴充方法。
- `zh-TW.xaml` / `en-US.xaml` 新增 `Trends.CustomRange`、`Goals.Add.Deadline` 等語言鍵。

## v0.5.8 - 2026-04-26

本版優化在地化提示文字並整理專案文件。

### 重點

- 調整 zh-TW 與 en-US 的「現金 / 信用卡 / 負債」新增對話框提示文字，反映目前「建立時即記錄初始條目」的流程，取代舊的「之後再去新增記錄」說法。
- 補上 `CHANGELOG.md` 中遺漏的 v0.4.1、v0.5.6、v0.5.7 條目。
- 移除 `docs/INDEX.md` 中失效的 `Downloads` 連結。

## v0.5.7 - 2026-04-26

本版優化投資組合對話框與表格的響應式體驗。

### 重點

- 新增紀錄對話框中的切換配對強制以橫向兩欄排列（窄寬時不再降回直向）。
- 透過覆寫預設的 `ListBoxItem` 範本，修正投資 Position 卡片 hover 矩形溢出。
- 將固定寬度的 `WrapPanel` 換成 `UniformGrid`，讓 position 統計儲存格能填滿可用寬度，無尾端空隙。
- Accounts / Liability DataGrid 第一欄欄頭由「資產」改為「名稱」；投資欄頭改為「標的」。
- 將 Accounts / Liability 儲存格範本重構為單列 `Grid`，使預設徽章能對齊整個儲存格高度的垂直中線。
- 新增紀錄底部按鈕順序改為符合 Windows 對話框慣例（取消在左，確認在右），並把「取消編輯」縮短為「取消」。

## v0.5.6 - 2026-04-25

本版修正負債建立流程的回歸問題。

### 重點

- 修正在某些狀態下，負債建立對話框未顯示貸款區塊的問題。

## v0.5.5 - 2026-04-24

本版優化啟動穩定性與復原行為。

### 重點

- 啟動時不再每次都因更新檢查而被阻塞。
- 僅在前次啟動未完成時才執行復原型啟動更新檢查。
- 維持日常啟動速度，同時保留損壞安裝的修復路徑。

### 內部變更

- 在 `App.xaml.cs` 新增 `startup.pending` 標記流程。
- 一般啟動流程改為主視窗顯示後在背景檢查更新。
- 復原更新路徑只在前次啟動疑似失敗時觸發。

## v0.5.4 - 2026-04-24

本版改善 Windows 應用程式品牌資產的細緻度。

### 重點

- 將模糊的 Windows 應用程式圖示路徑替換為專屬的多尺寸 Windows `.ico`。
- 應用程式 / 視窗圖示改為 Windows 專用資產，不再沿用網頁 favicon。

### 內部變更

- 新增 `Assets/windows/assetra-app.ico`。
- 更新 WPF 專案的圖示串接與主視窗圖示資源。

## v0.5.3 - 2026-04-24

本版在發生損壞安裝事件後，導入「啟動優先」的更新安全網。

### 重點

- 啟動時於主視窗開啟前先檢查更新。
- 啟動失敗時嘗試自我修復更新。

### 內部變更

- 此版本為過渡性安全版本，後續在 `v0.5.5` 進一步精修。

## v0.5.2 - 2026-04-24

本版修正啟動崩潰並強化幣別資料載入。

### 重點

- 修正初始 UI 載入時因徽章樣式造成的崩潰。
- 強化 Frankfurter 匯率解析以容忍缺失的 JSON 欄位。

### 內部變更

- `PortfolioBadgeBase` 不再透過脆弱的啟動路徑解析 `CornerRadius`。
- `CurrencyService` 採用容忍式 JSON 解析與安全 fallback。

## v0.5.1 - 2026-04-24

本版修正啟動畫面 / 圖示啟動可靠性問題。

### 重點

- 修正因啟動畫面圖示資源載入造成的啟動失敗。
- 改善打包安裝版本的啟動穩定性。

### 內部變更

- 啟動畫面圖示載入由靜態 XAML 資源解析改為以程式碼處理。
- 收緊圖示資產的資源打包規則。

## v0.5.0 - 2026-04-24

本版加入信用卡流程，並進行大規模響應式 UI 整修。

### 重點

- 新增信用卡資產與交易流程。
- 重組品牌資產與套件 logo 流程。
- 修飾 portfolio、alerts、settings、shell 與對話框版面的響應式表現。

### 內部變更

- 新增信用卡 workflow、schema 支援與回歸測試。
- 重做許多 WPF 版面，以更好支援較大的 `UiScale` 與較窄的寬度。

## v0.4.1 - 2026-04-23

本版新增 Fugle API key 設定的應用內指引。

### 重點

- 新增可從 Settings 頁面開啟的 Fugle 說明對話框。
- 在 zh-TW 與 en-US 提供設定步驟，使用者不必離開 app 即可完成設定。

## v0.4.0 - 2026-04-23

本版專注於更安全的 portfolio 編輯與可設定的市場資料來源。

### 重點

- 新增 `Fugle` 作為可設定的即時報價與歷史價格來源。
- 新增 `Settings` 欄位，分別管理報價來源、歷史來源與本機 Fugle API key 儲存。
- 新增於 Git 之外安全設定 Fugle API key 的文件。
- 重做記錄編輯流程：
  - 安全編輯模式
  - 建立修訂版
  - 取代原紀錄 / 兩者保留
- 將通用歡迎橫幅替換為任務導向的設定提示。

### 內部變更

- `StockScheduler` 可使用 Fugle，並在失敗時 fallback 至 TWSE/TPEX 官方來源。
- `DynamicHistoryProvider` 支援 `fugle`，與 `twse`、`yahoo`、`finmind` 並列。
- `PortfolioViewModel` 與測試保留向後相容的建構路徑，同時改用較新的 application 層服務。

## v0.3.0 - 2026-04-22

本版是 Assetra 第一個聚焦於架構的里程碑。

### 重點

- 加入更清晰的 `Core -> Application -> Infrastructure -> WPF` 結構。
- 將大多數 `Portfolio` 變更流程移入 application workflow services。
- 將 summary / load / history / query 職責移入專屬的 application services。
- 模組化啟動、schema migration、repository 初始化的責任。
- 在 `Portfolio` UI 部分引入較薄的 WPF 端 controllers / sub-viewmodels。
- 將 `Alerts` 重新置於 application 層介面（`IAlertService`）之後。
- 新增 workflow 層級測試與架構文件。

### 內部變更

- `PortfolioViewModel` 將更多行為委派給：
  - workflow services
  - query services
  - WPF 端 controllers / sub-viewmodels
- `FinancialOverviewViewModel` 改為透過 application query service 讀取資料。
- `.superpowers/` 本機工具產物已加入忽略，且不再追蹤。

## v0.2.0

application 層與架構整理工作之前較早期的產品里程碑。
