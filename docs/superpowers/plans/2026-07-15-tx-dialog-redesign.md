# 新增交易對話框全面重構 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓「新增交易」對話框輸入更簡單、寫入更準確：極簡快樂路徑、類型 chips、跨幣別結算卡入主流程＋溢價檢查，同時把九張常駐表單改為懶切換、收斂重複元件與 VM 歸屬——確認寫入層行為 bit-identical 不變。

**Architecture:** 原地分波重構（非並行新殼）。Wave 0 先用特徵測試鎖住確認/寫入層行為；後續每一波（共用元件 → VM 收斂 → 懶切換 → 新版面）都在既有測試綠燈的保護下進行，每波獨立可出貨、使用者目視驗收。

**Tech Stack:** WPF (.NET 10, `net10.0-windows10.0.19041.0`, `UseWPF`)、CommunityToolkit.Mvvm（`ObservableObject`/`[RelayCommand]`）、xUnit + Moq、SQLite。設計文件：`docs/superpowers/specs/2026-07-15-tx-dialog-redesign-design.md`。

---

## 分波總覽與驗收門檻

| 波 | 內容 | 風險 | 門檻（進下一波前） |
|---|---|---|---|
| **0** | 特徵測試基準線（鎖住 Buy/Sell/Dividend 確認輸出＋同幣別 fx 守門） | 無 | 全套件綠、基準線 commit、**使用者確認基準線覆蓋足夠** |
| **1** | 共用元件（FormField／TxFeeField／TxCashAccountPicker／SettlementCard／單一 footer）換入現有表單，行為零變化 | 低 | 全套件綠（含 Wave 0）、建置零警告、使用者目視現有表單無變化 |
| **2** | VM 收斂：Buy 脫離 `AddAssetDialog.*` → Sell 收斂，一張一張搬 | **高** | 每張表單遷完 Wave 0 特徵測試 **bit-identical** 綠、使用者目視 |
| **3** | ContentControl＋DataTemplate 懶切換，刪 ~20 個 `TxTypeIsXxx` | 中 | 全套件綠、九種類型切換手測不丟資料、使用者目視 |
| **4** | 新版面：chips、極簡預設、結算卡入主流程＋溢價檢查、ⓘ 收斂、刪交易幣別列 | 中 | 全套件綠＋新測試綠、使用者驗收；全部完成後一起發版 |

**每一波開始前**：先在 worktree 建置（主 checkout 可能被 VS 鎖 DLL——見 memory `wpf-rebuild-while-running-locks-dlls`）。**每一波結束**：`dotnet build Assetra.slnx` 零警告 + `dotnet test Assetra.Tests/Assetra.Tests.csproj` 全綠 + commit（中文訊息、無 AI 署名）+ 使用者目視驗收。

**Waves 1–4 的逐步程式碼在各波開工時 JIT 定稿**：本文件把 Wave 0 寫成可直接執行的 bite-sized TDD；Waves 1–4 給出完整的檔案地圖、任務分解、介面契約與驗收方式，但其 step 級程式碼（新控制項 XAML、VM 欄位搬移的最終形）會在**該波開工、且前一波已落地**時，依當時真實碼況補上——因為它們直接依賴前一波的產物，提早捏造只會與實作對不上。

---

## 檔案地圖

**Wave 0（只加測試）**
- Modify: `Assetra.Tests\WPF\TransactionDialogViewModelTests.cs`（沿用既有 `CreateVm`/`MakePosition`/`MakeCashAccount`/`CreateSellTrade`/`StaticBuyContext` helpers，新增 `#region Characterization — Wave 0 refactor baseline`）
- 可能 Modify/Create: Trade 純方法測試（`WithSameCurrencyFxCleared`）— 位置依既有 Trade 測試檔

**Wave 1（共用元件，新建控制項）**
- Create: `Assetra.WPF\Features\Portfolio\Controls\TxForms\Shared\FormField.xaml`(+`.cs`)
- Create: `...\Shared\TxFeeField.xaml`(+`.cs`)
- Create: `...\Shared\TxCashAccountPicker.xaml`(+`.cs`)
- Rename/Refactor: `...\TxForms\CrossCurrencyOverlay.xaml` → `SettlementCard.xaml`（含溢價列佔位，Wave 4 才接資料）
- Modify: 九張 `TxForms\*.xaml`（換用新控制項）、`AddRecordDialog.xaml`（footer 收斂成單一自適應版）

**Wave 2（VM 收斂）**
- Modify: `SubViewModels\Tx\BuyTxViewModel.cs`（長出 `Quantity`/`Price`/費用預覽屬性）、`AddAssetDialogViewModel.cs`（買入路徑改讀 `Buy.*`）、`SubViewModels\Tx\SellTxViewModel.cs`（`Price` 收斂）
- Create: 費用計算共用 helper（`Assetra.Application` 或 WPF 端，依現有 preview 邏輯所在層）
- Modify: `BuyTxForm.xaml`/`SellTxForm.xaml`（重新指向 `Buy.*`/`Sell.*`）

**Wave 3（懶切換）**
- Modify: `AddRecordDialog.xaml`（form host → `ContentControl` + `DataTemplate` selector）、`TransactionDialogViewModel.cs`（刪 `TxTypeIsXxx` 與通知清單）
- Create: `TxTypeTemplateSelector.cs`

**Wave 4（新版面）**
- Modify: `AddRecordDialog.xaml`（類型 chips、刪交易幣別列、結算卡入主流程）、各 TxForm（成交明細標題旁切換、ⓘ tooltip 收斂）
- Modify: `SettlementCard` VM（溢價檢查）、`zh-TW.xaml`＋`en-US.xaml`（新 key）、AppSettings（記住上次扣款帳戶，`raiseChanged:false`）

---

## Wave 0：特徵測試基準線

**為什麼先做**：Waves 1–3 是「行為不變」的重構，Wave 4 才動行為。要有一張網先釘住「同樣輸入 → 同樣寫入請求」，特別是跨幣別 FX/單價推算與 GROSS 語意——這正是歷史上 fx_rate 壞掉污染成本的地方。

**測試座標（已由探索確認）**：確認層純 async、無 Dispatcher/DB/static，workflow 全走注入介面，可純單元測試。斷言對象是「建出來的請求 DTO」：
- Buy → `Mock<IAddAssetWorkflowService>.ExecuteStockBuyAsync` 的 `StockBuyRequest`（欄位：`Price/Quantity/FxRate/ActualCashAmount/SettlementCurrency/…`）。
- Sell → `Mock<ISellWorkflowService>.RecordAsync` 的 `SellWorkflowRequest`（`SellPrice/SellQuantity/ActualCashAmount/FxRate`）。
- Dividend → `Mock<ITransactionWorkflowService>.RecordCashDividendAsync` 的 `CashDividendTransactionRequest`（`PerShare/TotalAmount/ActualCashAmount/FxRate`）。

### Task 0.1：確立既有基準線覆蓋並全綠

**Files:** Modify `Assetra.Tests\WPF\TransactionDialogViewModelTests.cs`（僅閱讀 + 加 region 標記）

- [ ] **Step 1：清點既有特徵覆蓋**。確認以下既有測試存在且與重構相關（皆在 `TransactionDialogViewModelTests.cs`）：
  - `ConfirmSellTx_TotalMode_DerivesUnitPriceFromTotalProceeds`（Sell GROSS：`price = TotalProceeds/qty`）
  - `ConfirmAdd_Stock_CrossCurrencyFxModeAcceptsFxWithoutActualCashAmount`（Buy fx 模式：`ActualCashAmount = price*qty*fx`）
  - 位於 ~1300 的 statement 模式 Mode-C 反推單價測試（`AddPrice=""`、`actualCashAmount:"32500"`、`fxRate:"32.5"` → `captured.Price == 100`）

- [ ] **Step 2：跑既有相關測試，確認綠**
  Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~TransactionDialogViewModelTests"`
  Expected: PASS（全數綠；記下總數當基準）

- [ ] **Step 3：在檔案的交易確認測試群開頭加註記 region**（純標記，不改邏輯）：
  ```csharp
  // ── Characterization baseline — 新增交易對話框重構（2026-07-15 spec）────────
  //   下列測試鎖住「輸入 → 寫入請求 DTO」的行為。Wave 1–3 為行為不變重構，
  //   這些必須在每次遷移後保持 bit-identical 綠；Wave 4 才允許新增（不放寬）驗證。
  ```

- [ ] **Step 4：Commit**
  ```bash
  git add Assetra.Tests/WPF/TransactionDialogViewModelTests.cs
  git commit -m "test(交易): 標記新增交易確認層特徵測試基準線"
  ```

### Task 0.2：鎖住 Buy 同幣別（fx=null、actualCash=null）

**Files:** Modify `Assetra.Tests\WPF\TransactionDialogViewModelTests.cs`

- [ ] **Step 1：寫測試**（緊貼既有 buy 測試群，沿用相同 Moq 樣式）
  ```csharp
  [Fact]
  public async Task Characterization_Buy_SameCurrency_NoFxNoActualCash()
  {
      // 同幣別（TWD 標的、無現金帳戶連動）：request 不得帶 FxRate / ActualCashAmount。
      StockBuyRequest? captured = null;
      var addWorkflow = new Mock<IAddAssetWorkflowService>();
      addWorkflow.Setup(w => w.SearchSymbols(It.IsAny<string>(), It.IsAny<int>())).Returns([]);
      addWorkflow.Setup(w => w.BuildBuyPreview(It.IsAny<BuyPreviewRequest>()))
          .Returns(new BuyPreviewResult(1000m, 0m, 1000m, 100m));
      addWorkflow
          .Setup(w => w.ExecuteStockBuyAsync(It.IsAny<StockBuyRequest>(), It.IsAny<CancellationToken>()))
          .Callback<StockBuyRequest, CancellationToken>((r, _) => captured = r)
          .ReturnsAsync(new StockBuyResult(
              new PortfolioEntry(Guid.NewGuid(), "2330", "TWSE", Currency: "TWD"),
              Commission: 0m, CommissionDiscountUsed: 1m, CostPerShare: 100m));

      var vm = new AddAssetDialogViewModel(
          addWorkflow.Object, Mock.Of<IAccountUpsertWorkflowService>(),
          Mock.Of<ITransactionWorkflowService>(), Mock.Of<ICreditCardMutationWorkflowService>());
      vm.AddAssetType = "stock";
      vm.AddSymbol = "2330";
      vm.AddSymbolCurrency = "TWD";
      vm.AddPrice = "100";
      vm.AddQuantity = "10";

      await vm.ConfirmAddCommand.ExecuteAsync(null);

      Assert.Equal(string.Empty, vm.AddError);
      Assert.NotNull(captured);
      Assert.Equal(100m, captured!.Price);
      Assert.Equal(10, captured.Quantity);
      Assert.Null(captured.FxRate);
      Assert.Null(captured.ActualCashAmount);
  }
  ```

- [ ] **Step 2：跑，確認綠**（釘住現況；此為既有行為，應直接綠）
  Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Characterization_Buy_SameCurrency_NoFxNoActualCash"`
  Expected: PASS。若失敗 → 停，代表我對現況理解有誤，先查清再繼續。

- [ ] **Step 3：Commit**
  ```bash
  git add Assetra.Tests/WPF/TransactionDialogViewModelTests.cs
  git commit -m "test(交易): 鎖住買入同幣別不帶 FxRate/ActualCashAmount"
  ```

### Task 0.3：鎖住 Buy 跨幣別「依帳戶明細」反推 FxRate（價格已填）

**Files:** Modify `Assetra.Tests\WPF\TransactionDialogViewModelTests.cs`

- [ ] **Step 1：寫測試**（statement 模式、價格已填、fx 留空 → 由扣款金額反推 fx）
  ```csharp
  [Fact]
  public async Task Characterization_Buy_CrossCurrency_StatementMode_DerivesFxFromActualCash()
  {
      // 依帳戶明細：填 價格+股數+實際扣款、fx 留空 → fx = (cash − fee) / (price × qty)。
      StockBuyRequest? captured = null;
      var addWorkflow = new Mock<IAddAssetWorkflowService>();
      addWorkflow.Setup(w => w.SearchSymbols(It.IsAny<string>(), It.IsAny<int>())).Returns([]);
      addWorkflow.Setup(w => w.BuildBuyPreview(It.IsAny<BuyPreviewRequest>()))
          .Returns(new BuyPreviewResult(1000m, 0m, 1000m, 100m));
      addWorkflow
          .Setup(w => w.ExecuteStockBuyAsync(It.IsAny<StockBuyRequest>(), It.IsAny<CancellationToken>()))
          .Callback<StockBuyRequest, CancellationToken>((r, _) => captured = r)
          .ReturnsAsync(new StockBuyResult(
              new PortfolioEntry(Guid.NewGuid(), "AAPL", "NASDAQ", Currency: "USD"),
              Commission: 0m, CommissionDiscountUsed: 1m, CostPerShare: 100m));

      var vm = new AddAssetDialogViewModel(
          addWorkflow.Object, Mock.Of<IAccountUpsertWorkflowService>(),
          Mock.Of<ITransactionWorkflowService>(), Mock.Of<ICreditCardMutationWorkflowService>())
      {
          BuyContext = new StaticBuyContext(
              cashAccountId: Guid.NewGuid(),
              cashAccountCurrency: "TWD",
              useCashAccount: true,
              settlementInputMode: "statement",
              actualCashAmount: "32500"),   // fxRate 留空
      };
      vm.AddAssetType = "stock";
      vm.AddSymbol = "AAPL";
      vm.AddSymbolCurrency = "USD";
      vm.AddPrice = "100";
      vm.AddQuantity = "10";

      await vm.ConfirmAddCommand.ExecuteAsync(null);

      Assert.Equal(string.Empty, vm.AddError);
      Assert.NotNull(captured);
      Assert.Equal(32_500m, captured!.ActualCashAmount);
      Assert.Equal(32.5m, captured.FxRate);   // (32500 − 0) / (100 × 10)
      Assert.Equal("TWD", captured.SettlementCurrency);
  }
  ```

- [ ] **Step 2：跑，確認綠**
  Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Characterization_Buy_CrossCurrency_StatementMode_DerivesFxFromActualCash"`
  Expected: PASS。若 `captured.FxRate` 觀察值與 32.5 不同 → **停下，把觀察到的實際值當基準釘住**（特徵測試以現況為準），並在 commit 訊息記錄實際公式。

- [ ] **Step 3：Commit**
  ```bash
  git add Assetra.Tests/WPF/TransactionDialogViewModelTests.cs
  git commit -m "test(交易): 鎖住買入依帳戶明細反推匯率 (cash−fee)/(price×qty)"
  ```

### Task 0.4：鎖住 Sell 跨幣別 FX 反推

**Files:** Modify `Assetra.Tests\WPF\TransactionDialogViewModelTests.cs`（沿用 `MakePosition`；Sell 走完整 VM）

- [ ] **Step 1：先讀** `SellPanelViewModel.cs:205-235` 確認 sell 的 FX 反推分支（`sellFx = grossInFunding / (sp * sellQty)`、`sellActual = sp * sellQty * fxOnly`）與觸發條件（跨幣別現金連動、statement/fx 模式）。

- [ ] **Step 2：寫測試**（statement 模式：填單價+股數+實際入帳、fx 留空 → 反推 fx）。以既有 `MakePosition`（TWD 部位）為基礎，但需一個外幣部位 + TWD 現金帳戶以觸發跨幣別；若 `MakePosition` 不支援外幣，改用直接建 `SellPanelViewModel` + `ExecuteSellFromTxDialogAsync`（見既有 `ExecuteSellFromTxDialogAsync_PassesTradeDateToWorkflow`，line ~1403，捕捉 `SellWorkflowRequest`）並提供 `GetActualCashAmount`/`GetFxRate` 委派。實際委派名稱與觸發條件以 Step 1 讀到的為準。
  斷言：`captured.SellPrice`、`captured.SellQuantity`、`captured.ActualCashAmount`、`captured.FxRate` 等於現況觀察值（跑一次後釘住）。

- [ ] **Step 3：跑，確認綠**
  Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Characterization_Sell_CrossCurrency"`
  Expected: PASS（以觀察值為基準）。

- [ ] **Step 4：Commit**
  ```bash
  git add Assetra.Tests/WPF/TransactionDialogViewModelTests.cs
  git commit -m "test(交易): 鎖住賣出跨幣別 FX 反推寫入請求"
  ```

### Task 0.5：鎖住 Cash Dividend（總額模式 perShare ＋跨幣別）

**Files:** Modify `Assetra.Tests\WPF\TransactionDialogViewModelTests.cs`

- [ ] **Step 1：寫測試**（完整 VM，捕捉 `CashDividendTransactionRequest`）
  ```csharp
  [Fact]
  public async Task Characterization_CashDividend_TotalMode_DerivesPerShare()
  {
      // 總額模式：perShare = total / 持有股數。
      CashDividendTransactionRequest? captured = null;
      var txWorkflow = new Mock<ITransactionWorkflowService>();
      txWorkflow.Setup(s => s.RecordCashDividendAsync(It.IsAny<CashDividendTransactionRequest>()))
          .Callback<CashDividendTransactionRequest>(r => captured = r)
          .Returns(Task.CompletedTask);

      var position = MakePosition();   // 見既有 helper：TWD、持倉股數已知
      var vm = CreateVm(
          positions: new ObservableCollection<PortfolioRowViewModel> { position });
      // 以 TransactionWorkflow 注入捕捉版：若 CreateVm 未暴露該注入點，改為在 deps 建構時傳入
      // txWorkflow.Object（見 §CreateVm 的 TransactionWorkflow 參數）。

      vm.TxType = "cashDividend";
      vm.Div.Position = position;
      vm.Div.InputMode = "total";      // 對照 perShare；確切字串以 DividendTxViewModel 為準
      vm.Div.TotalInput = "1000";

      await vm.ConfirmTxCommand.ExecuteAsync(null);

      Assert.Equal(string.Empty, vm.TxError);
      Assert.NotNull(captured);
      // perShare = 1000 / position.Quantity；以 MakePosition 的實際股數計算後釘住
      Assert.Equal(1000m, captured!.TotalAmount);
  }
  ```
  註：`CreateVm` 目前用 `Mock.Of<ITransactionWorkflowService>()`；本測試需要**可捕捉**的版本。第 1 步先確認 `CreateVm` 是否接受注入 `ITransactionWorkflowService`；若否，直接用 `TransactionDialogDependencies` 就地建構（複製 `CreateVm` 內容、把 `TransactionWorkflow` 換成 `txWorkflow.Object`）。`InputMode`/`TotalInput` 的確切屬性名以 `DividendTxViewModel.cs` 為準（Step 0 先讀）。

- [ ] **Step 2：跑，確認綠**（perShare 期望值跑一次後依 `position.Quantity` 釘死）
  Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Characterization_CashDividend_TotalMode_DerivesPerShare"`
  Expected: PASS。

- [ ] **Step 3：Commit**
  ```bash
  git add Assetra.Tests/WPF/TransactionDialogViewModelTests.cs
  git commit -m "test(交易): 鎖住現金股利總額模式反推每股金額"
  ```

### Task 0.6：鎖住同幣別 fx 守門（`WithSameCurrencyFxCleared`）

**Files:** Modify/Create Trade 純方法測試（先找既有 Trade 測試檔；`Trade.WithSameCurrencyFxCleared()` 在 `Assetra.Core` 的 Trade model 上）

- [ ] **Step 1：讀** `Trade.WithSameCurrencyFxCleared` 實作與既有測試。若已被測試覆蓋 → 記錄測試名、跳到 Step 4。

- [ ] **Step 2：若無覆蓋，寫純方法測試**（以既有 Trade 測試的建構樣式為準）：
  - 同幣別（instrument == settlement）→ 回傳的 Trade `FxRate` 為 `null`。
  - 不同幣別 → `FxRate` 保留原值。

- [ ] **Step 3：跑，確認綠**
  Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~WithSameCurrencyFxCleared"`
  Expected: PASS。

- [ ] **Step 4：Commit**
  ```bash
  git add -A
  git commit -m "test(交易): 鎖住同幣別交易 fx 清零守門"
  ```

### Task 0.7：Wave 0 收尾 — 全套件綠＋使用者驗收

- [ ] **Step 1：全套件**
  Run: `dotnet test Assetra.Tests/Assetra.Tests.csproj`
  Expected: 全綠；記下總測試數當「基準線」。
- [ ] **Step 2：建置零警告** `dotnet build Assetra.slnx`。
- [ ] **Step 3：使用者驗收門檻** — 向使用者確認：基準線是否覆蓋你在意的情境（尤其你最常用的台股買入、美股複委託買入）。不足則在此波補測試，**不進 Wave 1**。

---

## Wave 1：共用元件（行為零變化）

**目標**：把跨表單複製貼上的區塊抽成控制項，換進現有九張表單，**不改任何行為**——Wave 0 全綠是唯一正確性判準。

**任務分解**（每個任務 = 建一個控制項 + 換進用到的表單 + 跑 Wave 0/全套件綠 + commit）：

- **T1.1 `FormField`**（label＋input＋error 模板）：`DependencyProperty` 暴露 `Label`、`Text`（`TwoWay`）、`ErrorText`、`Placeholder`。先換 1 張表單的 1 個欄位驗證外觀一致 → 再全面換。
- **T1.2 `TxFeeField`**：包 `TxFee`/`TxFeeError`/`FeeOverrideHint`；換入 7 張含手續費的表單。
- **T1.3 `TxCashAccountPicker`**：包 checkbox＋`CashAccounts` 下拉＋「將新增」提示；換入 Buy/Sell/CashDividend/Loan。
- **T1.4 `CrossCurrencyOverlay` → `SettlementCard`**：更名 + 保留現有 4 個 DP 與 DataContext 機制，**預留溢價列容器**（Wave 4 才綁資料）。三處用點（Buy/Sell/Div）同步改名。
- **T1.5 Footer 收斂**：`AddRecordDialog.xaml` 桌面版＋窄版兩份 footer → 單一自適應版（用既有 `WidthScaleThresholdToBooleanConverter` 控制排列，不再複製按鈕）。

**每個任務的 step 樣板**：① 建/改控制項 XAML+cs（JIT 定稿）② 換入目標表單 ③ `dotnet build Assetra.slnx` 零警告 ④ `dotnet test`（Wave 0 必綠）⑤ commit `refactor(交易): 抽出 <控制項> 取代重複區塊`。
**風險守則**：`TxCashAccountPicker`/`SettlementCard` 靠「三個 sub-VM 有同名屬性」的隱性綁定——換名或改綁定會**執行期**靜默失敗（binding error，不是編譯錯）。每次換入後**實跑 app 目視**該表單，不能只靠編譯。
**門檻**：全套件綠＋建置零警告＋使用者目視「現有每張表單外觀/行為無變化」。

---

## Wave 2：VM 收斂（風險最高，最小步伐）

**目標**：把經濟欄位歸給 sub-VM——Buy 脫離借用 `AddAssetDialog.*`、Sell 單價從共用 `TxAmount` 收進 `Sell.Price`。**確認寫入路徑一行不動**，僅改「值的來源」。

**鐵律**：每搬一張表單，Wave 0 特徵測試必須 **bit-identical 綠**（同輸入→同 `StockBuyRequest`/`SellWorkflowRequest`）。任何一顆變紅＝立即回退該步、查清再走。

**任務分解**：
- **T2.1 費用計算抽共用 helper**：目前 Buy 的費用預覽走 `IAddAssetWorkflowService.BuildBuyPreview`；先確認「買入表單」與「新增資產對話框」是否算同一套。抽成單一被兩者呼叫的 helper（不是複製），加單元測試鎖住輸出。
- **T2.2 `BuyTxViewModel` 長出自有欄位**：`Quantity`/`Price`/費用預覽屬性，先與 `AddAssetDialog.*` **並存**（雙寫或轉發），特徵測試綠。
- **T2.3 `BuyTxForm.xaml` 綁定改指 `Buy.*`**：一次一個欄位，每個欄位改完跑特徵測試綠。
- **T2.4 移除 Buy 對 `AddAssetDialog.*` 的借用**：確認路徑改讀 `Buy.*` 組 `StockBuyRequest`；`AddAssetDialogViewModel`（獨立「新增資產」對話框）**完全不動**。
- **T2.5 Sell 單價 `TxAmount` → `Sell.Price`**：比照 T2.2–T2.4。
**門檻**：每張表單遷完，Wave 0 全綠（bit-identical）＋建置零警告＋使用者目視。

---

## Wave 3：懶切換（刪常駐九表單機制）

**目標**：form host 從「九張常駐 + `TxTypeIsXxx` 布林收合」改為 `ContentControl` + `DataTemplate` selector（TxType 字串挑模板），刪除 ~20 個 `TxTypeIsXxx` 屬性與 `OnTxTypeChanged` 手動通知清單。

**任務分解**：
- **T3.1 `TxTypeTemplateSelector`**：`TxType` 字串 → 對應 `DataTemplate`（每張表單一個 `DataTemplate`，`DataType` 綁 sub-VM 或以 key 選）。
- **T3.2 `AddRecordDialog.xaml`**：form host `StackPanel`（現 456–503）換成 `ContentControl Content={Binding}` + selector；九個 `<txforms:*/>` 移進 `ContentControl.Resources` 的 `DataTemplate`。
- **T3.3 刪 `TxTypeIsXxx`**：移除屬性與 `OnTxTypeChanged`（1641–1685）的通知清單；`AvailableTradeTypes`/`_confirmDispatch` 不動。
- **T3.4 切換不丟資料驗證**：手測——填一半 Buy → 切 Sell → 切回 Buy，內容仍在（sub-VM 常駐）。加自動化測試：切換後 sub-VM 狀態保留。
**門檻**：全套件綠＋九種類型逐一手測切換不丟資料＋使用者目視。

---

## Wave 4：新版面（唯一動行為的一波）

**目標**：落地版面決策（mockup v2）＋新增驗證（只嚴不鬆）。

**任務分解**：
- **T4.1 類型 chips**：類型下拉 → `買入`/`賣出`/`更多 ▾`；「更多」彈出選單收其餘七種。`AvailableTradeTypes` 分組。
- **T4.2 刪交易幣別列**：幣別併入資產 chip（投資唯讀）／跟隨帳戶（現金/負債唯讀）；保留無帳戶需手選幣別的表單內小下拉。沿用 `IsTxCurrencyEditable` 語意。
- **T4.3 成交明細切換移到區塊標題旁**（Buy/Sell 同位置）；結算摘要列常駐。
- **T4.4 結算卡入主流程**：跨幣別（成交幣別 ≠ 扣款帳戶幣別）時 `SettlementCard` 顯示於主流程，移除「自動展開進階」workaround。IB 同幣別（USD→USD）不觸發。
- **T4.5 溢價檢查**（新驗證，做在 `SettlementCard` VM）：
  - `溢價% = (實際扣款 − 價金×市場匯率) / (價金×市場匯率)`；市場匯率查交易日 `fx_rate_history`（Yahoo）→ 即時匯率 → 查無**靜默跳過**。查詢在背景執行緒，完成後更新綁定屬性（單屬性通知可跨執行緒；**不得**在背景動綁定集合——見 memory `ui-bound-observablecollection-cross-thread`）。
  - 分級：≤3% 綠✓／3–10% 黃色警告不擋／>10% 確認鈕變「仍要儲存」需二次確認。賣出/股利檢查絕對偏差。
  - 加單元測試：分級邏輯 + 無匯率跳過（不擋存）。
- **T4.6 極簡預設**：日期今天；「記住上次扣款帳戶」存 AppSettings 時 `raiseChanged:false`（memory `settings-changed-feedback-loop-landmine`）；說明文字收 ⓘ tooltip。
- **T4.7 語言檔**：新 key（溢價訊息、費用合計標籤、更多選單）`zh-TW.xaml`＋`en-US.xaml` **兩檔同步**。
**門檻**：全套件綠＋新測試綠＋使用者驗收各情境（台股、IB、複委託、溢價三級）；全部完成後一起發版（依 memory `release-process`）。

---

## Self-Review（對照 spec）

- **Spec §2 決策**：範圍(全面重構)→Waves 全涵蓋；寫入不變→Wave 0 鎖 + Wave 2 鐵律；極簡快樂路徑→T4.6；買賣第一排→T4.1；跨幣別觸發規則→T4.4；預設依帳戶明細→T4.4/T0.3；費用不拆→維持單一 fee 欄（無 schema 任務，正確）。✓
- **Spec §3 版面**：chips T4.1、刪幣別列 T4.2、切換移位 T4.3、結算摘要列 T4.3、扣款一列 T1.3、結算卡入主流程 T4.4、進階收斂 T4.3/T4.6、說明收 ⓘ T4.6。✓
- **Spec §4 架構**：懶切換 Wave 3、共用元件 Wave 1、VM 收斂 Wave 2。✓
- **Spec §5 驗證**：溢價檢查 T4.5、同幣別守門 T0.6、既有驗證保留（Waves 1–3 不動行為）。✓
- **Spec §6 測試**：Wave 0 特徵矩陣（Buy 5／Sell／Div／守門）——**已知精確公式者給出 exact 期望值（0.2/0.3）；其餘以「跑一次觀察後釘住」的特徵測試法處理（0.4/0.5），這是特徵測試的正確做法，非佔位**。✓
- **Spec §8 注意事項**：語言檔雙寫 T4.7、`raiseChanged:false` T4.6、Dispatcher 規則 T4.5、worktree 建置（總覽註記）。✓
- **型別一致性**：`StockBuyRequest`（`Price/Quantity/FxRate/ActualCashAmount/SettlementCurrency`）、`SellWorkflowRequest`（`SellPrice/SellQuantity`）、`CashDividendTransactionRequest`（`PerShare/TotalAmount`）、`StaticBuyContext`（`settlementInputMode/actualCashAmount/fxRate/useCashAccount/cashAccountCurrency`）——均與探索確認的實際簽章一致。✓
- **已知留待 JIT**：Waves 1–4 的 step 級控制項/VM 程式碼（依賴前一波產物），及 0.4/0.5 中 `SellPanelViewModel` FX 委派名、`DividendTxViewModel` 的 `InputMode/TotalInput` 屬性名——各任務 Step 1 明列「先讀確認」。此為刻意設計，非遺漏。
