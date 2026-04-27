# Assetra 測試資料

各檔案皆為 2026 年 4 月資料，可直接餵入 Import / Reconciliation / Reports 三條流程驗證 end-to-end。

## 檔案清單

| 檔案 | 格式 | 編碼 | 用途 |
|------|------|------|------|
| `import-generic-bank.csv` | Generic | UTF-8 | 銀行流水（Income / Expense 各 5 筆） |
| `import-generic-securities.csv` | Generic | UTF-8 | 券商買賣（Buy 4 筆 / Sell 1 筆） |
| `reconciliation-statement.csv` | Generic | UTF-8 | 對帳單；故意與 bank.csv 有 1 筆 AmountMismatch（680→700）+ 1 筆 Missing（4/29 7-11） |
| `cathay-bank-utf8.csv` | CathayUnitedBank | UTF-8 | 國泰銀行格式範本（需轉 Big5 才能被 detector 認出） |
| `yuanta-securities-utf8.csv` | YuantaSecurities | UTF-8 | 元大證券格式範本（需轉 Big5） |
| `convert-to-big5.ps1` | — | — | UTF-8 → Big5 轉碼小工具 |

## 使用流程

### 1. Import 流程

1. 開啟 Assetra → Import 分頁
2. 選 `import-generic-bank.csv`，預期 detector 認 `Generic`
3. 預覽 10 筆 → Apply → 進入 Trade Journal

### 2. Reconciliation 流程（v0.10 新功能）

1. 先依「1. Import 流程」匯入 `import-generic-bank.csv`
2. Import → Reconciliation 分頁 → 「新建 Session」
3. 來源選「上傳新檔」→ 選 `reconciliation-statement.csv`
4. Compute Diffs，預期：
   - **AmountMismatch ×1**：4/12 星巴克 -680（trade）vs -700（statement）
   - **Missing ×1**：4/29 7-11 -450（statement 有，trade 無）
   - 其餘 9 筆完全相符 → 不出 diff
5. 對 Missing 列點「Create Trade」→ 寫入新 trade
6. 對 AmountMismatch 點「Overwrite from Statement」→ trade 金額改成 -700
7. SignOff

### 3. Reports 流程（v0.11 新功能）

匯入 bank + securities 兩檔後，開啟 Reports 分頁：

- **Income Statement**：Income ≈ 62,000（薪資 50k + 股息 12k）；Expense ≈ 34,860
- **Balance Sheet**：依現金帳戶設定而定
- **Cash Flow**：Operating ≈ +27,140、Investing ≈ -107,150（4 Buy − 1 Sell）

每張表右上角有 PDF / CSV 匯出鈕。

## Big5 編碼轉換

5 家銀行 + 5 家券商的 parser config 預設 Big5 編碼。要測試這些格式：

```powershell
# 在 samples/ 目錄下
pwsh ./convert-to-big5.ps1 -InputPath ./cathay-bank-utf8.csv
# → 產生 cathay-bank-big5.csv
```

或一次轉全部：

```powershell
Get-ChildItem *-utf8.csv | ForEach-Object {
    pwsh ./convert-to-big5.ps1 -InputPath $_.FullName
}
```

注意：Detector 也會看檔名 signature（如 `cathay` / `國泰`），檔名請保留關鍵字。
