# 雲端同步設定指南

> 雲端同步在 v0.20.0–v0.21.0 出貨。本指南說明如何啟用、操作與排除問題。

## 一、雲端同步是什麼

Assetra 的雲端同步把本地 SQLite 中的 8 種資料（分類、交易、資產、資產群組、資產事件、Portfolio、自動分類規則、Recurring transactions）以**端到端加密**的形式上傳到使用者自選的後端，並從後端拉回其他裝置的變更。設計重點：

- **使用者控制金鑰**：加密金鑰由使用者輸入的「密語（Passphrase）」經 PBKDF2 衍生，**密語不上傳、不寫入 settings**。後端拿到的是密文，無法解密。
- **Last-Write-Wins + manual conflict drain**：版本相同但內容不同時自動依 `last_modified_at` 解；無法自動解的進入 `Settings → Sync → Conflicts` 面板手動處理。
- **Soft delete tombstone**：刪除以墓碑形式同步，不會在另一台裝置上「復活」。
- **per-device materialized 不入同步**：`PendingRecurringEntry` 是每台裝置自行確認的待入帳佇列，依設計**不**同步。

## 二、首次設定

### 1. 準備後端

Assetra 同步協定走標準 HTTPS，後端只需提供兩個端點（GET `/sync` 拉、POST `/sync` 推），規格見 [`Assetra.Infrastructure/Sync/Http/`](../../Assetra.Infrastructure/Sync/Http/)。可選方案：

- **自架**：任何能跑加密 blob 儲存的 endpoint（最簡單就是 NodeJS / Python 包一層 S3）。
- **第三方**：未來 v1.0+ 會提供 reference 後端，目前需自架。

備齊：
- Backend URL（如 `https://your-backend.example.com/sync`）
- Auth Token（後端認證用 bearer token，可空）

### 2. 進入 Sync 設定

`Settings → Sync` 分頁：

| 欄位 | 說明 |
|---|---|
| Enable Sync | 開關。關閉後 `SyncCoordinator` 拒絕執行。 |
| Backend URL | 後端 endpoint。空則拒絕執行。 |
| Auth Token | 後端 bearer token（可空）。 |
| Passphrase | 加密密語。**只活在記憶體**，每次手動同步要重新輸入；除非勾選下面的快取。 |
| Cache passphrase for background | 勾選後密語放進 `SyncPassphraseCache`，讓 `BackgroundSyncService` 在 `Interval` 時自動觸發。重啟 app 後快取清除。 |
| Sync interval (minutes) | 0 = 不自動同步；> 0 啟用背景同步。 |

### 3. 第一次手動同步

按 `Sync Now`：

1. `SyncCoordinator` 檢查 `SyncEnabled` + `SyncBackendUrl`。
2. 首次執行自動產生 `SyncDeviceId`（GUID）+ `SyncPassphraseSalt`（16-byte），透過 `IAppSettingsService.SaveAsync` 寫入 settings。
3. `Pbkdf2KeyDerivationService` 從 passphrase + salt 衍生 32-byte AES key。
4. `EncryptingCloudSyncProvider` 把每筆 envelope 用 AES-GCM 加密後送 `HttpCloudSyncProvider`。
5. 拉回對方裝置的變更，`LastWriteWinsResolver` 解衝突，剩下的進 manual drain。
6. UI 顯示 `LastSyncAt / LastPulled / LastPushed / AutoResolved / ManualConflicts`。

### 4. 第二台裝置

- 在第二台裝置安裝 Assetra（不需匯入第一台的資料）。
- `Settings → Sync` 填**相同**的 Backend URL + Auth Token + **相同的 Passphrase**。
- 按 `Sync Now`：第二台會產生**自己的** DeviceId + Salt（不同於第一台），但 PBKDF2 用同一個 passphrase + 後端配發的相同 salt → 相同 AES key → 能解密第一台推上來的資料。

> ⚠️ **Salt 同步機制**：v0.21.0 salt 由各裝置首次同步時各自產生，目前需確保兩台裝置在首次連線時能讀到後端的 reference salt（後端職責，超出本指南）。實務上最簡作法：先在第一台同步並建立後端記錄，第二台 fresh install 時手動把第一台的 `SyncPassphraseSalt`（base64）填入。

## 三、Conflict 解決

當兩台裝置在 LWW 解不開時（譬如同一秒從不同裝置改同一筆），衝突進入 `Settings → Sync → Conflicts`：

| 動作 | 結果 |
|---|---|
| Keep Local | 本地版本當作勝出，下次推上去 |
| Keep Remote | 套用對方版本到本地 |
| Skip | 留在佇列，下次再決定 |

i18n keys：`Settings.Sync.Conflicts.*`（zh-TW / en-US 902/902 對齊）。

## 四、安全模型

- **密語不上傳**：passphrase 只在記憶體 → KDF → AES key → 用完即丟。`AppSettings` 不存 passphrase。
- **AES-GCM 認證加密**：篡改密文會 throw `CryptographicException`，不會默默套到本地。
- **Salt 16-byte，Key 32-byte**：符合 NIST SP 800-132 對 PBKDF2 的建議下限（iterations 由 `Pbkdf2KeyDerivationService` 內部設定）。
- **密語遺失 = 資料無法復原**：後端拿到的是密文，Assetra 也救不回來。請務必妥善保管 passphrase。

## 五、Troubleshooting

| 症狀 | 可能原因 | 處理 |
|---|---|---|
| `Sync is not enabled in settings.` | 沒勾 Enable Sync | 勾選並 Save |
| `Backend URL is not configured.` | URL 空 | 填入並 Save |
| `HttpRequestException` / 5xx | 後端不可達 / 內部錯誤 | 檢查後端 log；確認 Auth Token |
| `OperationCanceledException` | 使用者取消或網路逾時 | 重試 |
| `CryptographicException` | Passphrase 與後端資料不符 | 確認 passphrase 拼字；換裝置時 salt 必須一致 |
| Conflicts 累積 | 兩端短時間都改同筆 | 進 Conflicts 面板逐筆解 |
| 第二台拉不到第一台資料 | Salt 不一致 → key 不同 → 解密失敗 | 把第一台 `SyncPassphraseSalt` 抄到第二台（v0.21.0 暫行作法） |

## 六、相關檔案

- `Assetra.WPF/Infrastructure/SyncCoordinator.cs` — 觸發點
- `Assetra.WPF/Features/Settings/SyncSettingsViewModel.cs` — UI binding
- `Assetra.WPF/Features/Settings/Conflicts/` — manual drain 面板
- `Assetra.Infrastructure/Sync/Http/HttpCloudSyncProvider.cs` — wire format
- `Assetra.Infrastructure/Sync/EncryptingCloudSyncProvider.cs` — AES-GCM 層
- `Assetra.Application/Sync/SyncOrchestrator.cs` — pull / resolve / push pipeline
- 衝突解析：`Assetra.Application/Sync/LastWriteWinsResolver.cs`
- 整合測試範例：`Assetra.Tests/Integration/Sync/SyncEndToEndIntegrationTests.cs`
