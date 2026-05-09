# Settings Schema Migration — v0.x → v1.0

**Status:** Spec / playbook
**Last updated:** 2026-05-09 (v0.27.0 → v1.0 transition planning)

## Why this exists

`AppSettings.cs` has accumulated 30+ fields across the v0.6 → v0.27 series. New fields land with safe defaults (positional record params), so deserializing an old `AppSettings.json` into a newer `AppSettings` record always works — but a v1.0 release should also remove deprecated fields and rename a few to the names we'd pick if writing it today. That's a breaking schema change and needs an explicit migration step.

## Current state (v0.27)

`AppSettingsService.LoadSettings()` reads `%APPDATA%/Assetra/settings.json`, deserialises directly into `AppSettings`, and falls back to `new AppSettings()` (all defaults) on parse error. There is no version field on disk; the schema is implicitly inferred from the JSON keys present.

## Migration target

1. Add `int SchemaVersion = 1;` to the `AppSettings` record (currently absent).
2. Bump on every breaking schema change (rename / remove field). Additive changes don't bump.
3. `AppSettingsService.LoadSettings()` reads `SchemaVersion`, runs the appropriate migration chain, then deserialises.

## v0 → v1 migration script (when v1.0 ships)

| Action | Field | Reason |
|---|---|---|
| Drop | `UsdTwdRate` | Superseded by `ExchangeRates` dictionary in v0.13. Existing reads honor the dictionary first, so removal is safe. |
| Rename | `LastFxRefreshUtc` → `Fx.LastRefreshUtc` | If we introduce nested settings sections in v1.0 (`Fx.*`, `Llm.*`, `Amt.*`). Decided per UI shape. |
| Move | `OcrTessdataPath`, `OcrLanguage` | → `Import.Ocr.*` if we nest. |
| Validate | `AmtRate` | Already clamped at runtime in v0.27; on v1 load also assert `0 ≤ AmtRate ≤ 1` and reject parse with backup of corrupt file as `settings.json.bak`. |
| Move LlmApiKey | `LlmApiKey` (plaintext) → OS credential store | Out of scope for v1.0; keep in JSON with a deprecation comment. Track as v1.1 follow-up. |

## Migration code stub (deferred to v1.0 PR)

```csharp
internal static class AppSettingsMigrations
{
    public const int CurrentSchemaVersion = 1;

    public static AppSettings Apply(JsonElement raw)
    {
        var version = raw.TryGetProperty("SchemaVersion", out var v) ? v.GetInt32() : 0;
        if (version >= CurrentSchemaVersion) return raw.Deserialize<AppSettings>()!;

        // v0 → v1
        if (version < 1)
        {
            // Drop UsdTwdRate (already redundant; no-op)
            // Rename LastFxRefreshUtc → Fx.LastRefreshUtc — only if v1 adopts nesting
            // (Keep flat for v1.0; revisit at v1.1)
        }

        var result = raw.Deserialize<AppSettings>() with { SchemaVersion = CurrentSchemaVersion };
        return result;
    }
}
```

## Backup strategy

When migration mutates anything, write a `settings.json.bak.<timestamp>` before saving the migrated version. Keep up to 5 backups (rolling).

## Acceptance criteria

- A v0.27 user's `settings.json` survives v1.0 first-run with no data loss.
- A corrupt JSON triggers a friendly error dialog + offers to reset to defaults.
- The new `SchemaVersion` field roundtrips correctly.
- The migration is unit-tested with sample v0.x JSON fixtures.

## When to do this

This is part of the v1.0 GA release prep — not a v0.x patch. Pair with the broader regression-test sweep (E2 in roadmap).
