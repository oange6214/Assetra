# Fugle API Key 申請與安全設定

這份文件說明如何申請 Fugle API key，以及之後在 `Assetra` 專案中如何安全保存，避免把祕密值提交到 GitHub。

## 目前狀態

截至目前版本，`Assetra` 已可在 **Settings** 中設定：

- `即時報價來源`
- `歷史資料來源`
- `Fugle API Key`

若你選擇 `Fugle` 作為資料來源，程式會從本機設定讀取 API key，不會寫入 GitHub。

目前其他可用台股資料來源仍包含：

- `TWSE`
- `TPEX`
- `Yahoo Finance`
- `FinMind`
- `CoinGecko`

為主。

因此，Fugle API key **不需要** 放進版本庫，也不應直接寫死在原始碼、`README`、`docs`、測試檔、或任何可被 commit 的設定檔中。

## 如何申請 Fugle API key

1. 前往 [Fugle Developer](https://developer.fugle.tw/)
2. 註冊或登入 Fugle 帳號
3. 進入開發者後台 / API key 管理頁
4. 建立新的 API key
5. 依需求確認你使用的是哪個方案
   - 基本用戶
   - 開發者
   - 進階用戶
6. 妥善保存 key，避免貼到公開 repo、issue、PR、截圖或聊天記錄中

官方文件可參考：

- [Fugle Developer Docs](https://developer.fugle.tw/docs/)
- [行情方案及價格](https://developer.fugle.tw/docs/pricing/)

## 在 Assetra 中的安全保存方式

建議優先順序如下。

### 1. 最推薦：環境變數

使用本機環境變數保存，例如：

- `FUGLE_API_KEY`

優點：

- 不會進 Git
- 適合本機開發與部署
- 之後若 `Assetra` 接入 Fugle，程式可直接從環境變數讀取

Windows PowerShell 範例：

```powershell
[System.Environment]::SetEnvironmentVariable("FUGLE_API_KEY", "<your-key>", "User")
```

設定後重新開啟終端機或 IDE。

### 2. 次佳：放在使用者目錄的本機設定檔

若未來 `Assetra` 要支援本機 secrets 檔，建議放在：

```text
%APPDATA%\Assetra\local-secrets.json
```

這類檔案不應放在 repo 內，也不應同步到 GitHub。

目前 `Assetra` 預設把設定存在：

```text
%APPDATA%\Assetra\settings.json
```

若你透過 `Settings` 畫面輸入 Fugle API key，會保存到這個本機檔案，而不是 repo 內。

建議格式：

```json
{
  "FugleApiKey": "<your-key>"
}
```

### 3. 不建議：直接寫進 repo 內檔案

不要把 API key 放進：

- `README.md`
- `docs/*.md`
- `appsettings.Development.json`
- `appsettings.Local.json`
- 測試常數
- 原始碼字串常數

即使檔案之後刪掉，也可能已經進入 Git 歷史。

## Git 安全規則

本 repo 已額外忽略以下常見本機祕密檔：

- `*.local.json`
- `.env.local`
- `local-secrets.json`

但要注意：**`.gitignore` 只能阻止未來新增，不會自動移除已經被追蹤的祕密檔**。

如果某個含 key 的檔案曾經被 commit，應立即：

1. 重新產生 / 失效舊 key
2. 從 Git 歷史中清除
3. 再改用環境變數或本機 secrets 檔

## 未來若要接入 Fugle

建議實作順序：

1. 新增 `Fugle` 專用 provider / client
2. 從環境變數或 `%APPDATA%\Assetra\local-secrets.json` 讀取 key
3. 若找不到 key，UI 顯示未配置狀態，但不要崩潰
4. 禁止將 key 寫入 log
5. 測試用假 key / mock，不使用真實 key

## 檢查清單

在提交任何與 Fugle 有關的變更前，先確認：

- API key 沒有出現在 `git diff`
- API key 沒有出現在 `README` 或 `docs`
- API key 沒有出現在測試檔與 log
- 實際讀取方式是環境變數或 repo 外本機檔
