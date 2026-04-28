# Sync Wire Protocol (v0.21.0)

`HttpCloudSyncProvider` 與雲端後端之間的 HTTP 契約。後端可由 Cloudflare Workers / Supabase Edge Functions / 自架 ASP.NET 等實作；只要符合本文件即可互換。

## 重要前提

- **客戶端已用 AES-256-GCM 加密 `payload`。** Server 看到的 `payload` 是 base64(nonce ‖ tag ‖ ciphertext)。Server 永遠不需解密、不持有金鑰。
- `entity_id` / `entity_type` / `version` / `deleted` 為明文，supplemental for indexing、conflict detection、cursor ordering。
- 所有時間欄位為 ISO 8601 含時區（`DateTimeOffset` 規範）。

## 認證

`Authorization: Bearer <token>`（optional；test backend 可省略）。Token 來源由 backend 決定（API key、JWT、…）。

## Endpoint 1：Pull

```
GET {baseUrl}/sync/pull[?cursor={cursor}]
```

**Response 200**

```json
{
  "envelopes": [
    {
      "entity_id": "uuid",
      "entity_type": "Trade",
      "payload": "base64-encrypted-blob",
      "version": {
        "version": 3,
        "last_modified_at": "2026-04-28T10:00:00+00:00",
        "last_modified_by_device": "dev-A"
      },
      "deleted": false
    }
  ],
  "next_cursor": "100"
}
```

**語意**

- `cursor` 缺省 = 從頭開始（第一次同步）。Provider-specific opaque string；客戶端只負責保存。
- Server 回傳所有 `cursor < x ≤ next_cursor` 範圍內的變更，依時間 / sequence 升序。
- 沒新變更時 `envelopes: []`、`next_cursor` 可保持原值或回 null。

## Endpoint 2：Push

```
POST {baseUrl}/sync/push
Content-Type: application/json
```

**Request body**

```json
{
  "device_id": "uuid",
  "envelopes": [
    {
      "entity_id": "uuid",
      "entity_type": "Trade",
      "payload": "base64-encrypted-blob",
      "version": { "version": 4, "last_modified_at": "...", "last_modified_by_device": "dev-A" },
      "deleted": false
    }
  ]
}
```

**Response 200**

```json
{
  "accepted": ["uuid-1", "uuid-3"],
  "conflicts": [
    {
      "local": { /* 客戶端送來的 envelope */ },
      "remote": { /* server 目前 entity 的版本 */ }
    }
  ],
  "next_cursor": "101"
}
```

**衝突偵測規則（server-side）**

對每個 incoming envelope：

1. 若 `entity_id` 不存在 → **接受**，寫入。
2. 若 `incoming.version.version > stored.version.version` → **接受**，覆寫。
3. 否則 → **回 conflict**，把目前 `stored` 放在 `remote`、incoming 放在 `local`。

> 客戶端會用 `LastWriteWinsResolver` 或 UI 介入處理 conflict，再以 bumped version 重新 Push。

## 錯誤回應

- `401 Unauthorized` — token 無效。
- `400 Bad Request` — 格式錯誤（缺欄位 / 非合法 UUID / 非合法 base64）。
- `5xx` — server 故障；客戶端應 retry with backoff。

任何非 2xx，`HttpCloudSyncProvider` 直接 throw `HttpRequestException`，由上層決定處理。

## Cloudflare Workers + R2 參考實作（pseudocode）

```js
// R2 物件 key 格式：envelopes/{entity_id}.json
// KV 索引（或 D1 SQLite）：(seq INTEGER PK, entity_id, entity_type, modified_at, version)

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/sync/pull" && request.method === "GET")
      return pull(url, env);
    if (url.pathname === "/sync/push" && request.method === "POST")
      return push(request, env);
    return new Response("Not Found", { status: 404 });
  }
};

async function pull(url, env) {
  const cursor = parseInt(url.searchParams.get("cursor") ?? "0", 10);
  const rows = await env.DB.prepare(
    "SELECT entity_id, entity_type, modified_at, version, device, deleted, seq " +
    "FROM index WHERE seq > ? ORDER BY seq ASC LIMIT 500"
  ).bind(cursor).all();

  const envelopes = [];
  let lastSeq = cursor;
  for (const r of rows.results) {
    const obj = await env.R2.get(`envelopes/${r.entity_id}.json`);
    envelopes.push(JSON.parse(await obj.text()));
    lastSeq = r.seq;
  }
  return Response.json({ envelopes, next_cursor: String(lastSeq) });
}

async function push(request, env) {
  const body = await request.json();
  const accepted = [];
  const conflicts = [];
  for (const incoming of body.envelopes) {
    const existingObj = await env.R2.get(`envelopes/${incoming.entity_id}.json`);
    if (existingObj) {
      const existing = JSON.parse(await existingObj.text());
      if (incoming.version.version <= existing.version.version) {
        conflicts.push({ local: incoming, remote: existing });
        continue;
      }
    }
    await env.R2.put(`envelopes/${incoming.entity_id}.json`, JSON.stringify(incoming));
    const seq = await nextSeq(env);
    await env.DB.prepare(
      "INSERT OR REPLACE INTO index (entity_id, entity_type, modified_at, version, device, deleted, seq) " +
      "VALUES (?, ?, ?, ?, ?, ?, ?)"
    ).bind(
      incoming.entity_id, incoming.entity_type,
      incoming.version.last_modified_at, incoming.version.version,
      incoming.version.last_modified_by_device, incoming.deleted ? 1 : 0, seq
    ).run();
    accepted.push(incoming.entity_id);
  }
  const cursor = await currentSeq(env);
  return Response.json({ accepted, conflicts, next_cursor: String(cursor) });
}
```

`nextSeq` / `currentSeq` 可以用 D1 一張單列計數表，或 Durable Object 提供強一致 counter。

## 為什麼選 R2 + Workers？

- **零 egress 費用**：跟 S3 / Supabase 比，個人 app 同步流量幾乎免費。
- **客戶端加密 → server 純 blob**：R2 不需懂資料格式，KV / D1 只需存 metadata。
- **Workers 可寫成 < 200 行 JS**：對個人開發者運維負擔最小。

替代方案（若使用者改變主意）：

- **Supabase**：直接拿 Postgres + RLS，conflict 偵測可用 trigger，但需信任他們持有 schema-aware 後端。
- **自架 ASP.NET**：需 VPS + TLS + 監控；本協議契約不變，只是 backend 換實作。
