# H3 — PortfolioViewModelFactory Extraction

**Status:** Planning  
**Estimated effort:** 8–16h  
**Priority:** High (eliminates duplication between DI wiring and test fixtures; unblocks easy multi-instance scenarios)

---

## Problem

`PortfolioViewModel` construction is currently spread across two diverging code paths:

1. **Production DI** — `PortfolioServiceCollectionExtensions.AddPortfolioContext` (50-line ctor call wiring 30+ services into `PortfolioServices` + `PortfolioUiServices` + nested `AddAssetDialogViewModel` + nested `SellPanelViewModel`).
2. **Tests** — `Assetra.Tests/WPF/PortfolioViewModelTests.cs` builds a parallel construction harness (mocked services, hand-rolled `PortfolioServices` records). Whenever production adds a dependency, tests have to mirror the change manually.

**Symptoms:**

- Adding a new dependency means editing **two** call sites; easy to miss one.
- Test fixtures grow boilerplate and obscure what the test actually exercises.
- No way to spawn a second `PortfolioViewModel` (e.g., in a future "compare two portfolios" view) without copy-pasting the wiring.
- `AllocationViewModel`, `DashboardViewModel`, `FinancialOverviewViewModel` all take `PortfolioViewModel` directly — circular-feeling DI graph.

---

## Target Design

A factory that owns the construction recipe and is itself DI-registered:

```csharp
public interface IPortfolioViewModelFactory
{
    PortfolioViewModel Create();
}

internal sealed class PortfolioViewModelFactory : IPortfolioViewModelFactory
{
    private readonly IServiceProvider _sp;
    public PortfolioViewModelFactory(IServiceProvider sp) => _sp = sp;
    public PortfolioViewModel Create() => new PortfolioViewModel(
        BuildPortfolioServices(_sp),
        BuildPortfolioUiServices(_sp));

    private static PortfolioServices BuildPortfolioServices(IServiceProvider sp) { … }
    private static PortfolioUiServices BuildPortfolioUiServices(IServiceProvider sp) { … }
}
```

**Test harness** becomes a sibling factory:

```csharp
internal sealed class PortfolioViewModelTestFactory
{
    public Mock<IStockService> Stock { get; } = new();
    public Mock<ITradeRepository> Trades { get; } = new();
    // … per-dep mocks exposed for assertion
    public PortfolioViewModel Build() => new PortfolioViewModel(
        new PortfolioServices(Stock.Object, …),
        new PortfolioUiServices(…));
}
```

Tests do `var fx = new PortfolioViewModelTestFactory(); fx.Trades.Setup(…); var vm = fx.Build();` — no more 30-line construction blocks per test.

---

## Migration Plan

### Phase 1 — Production extraction (no behaviour change)

1. Create `Assetra.WPF/Features/Portfolio/PortfolioViewModelFactory.cs` (concrete only, no interface yet).
2. Move the entire `services.AddSingleton<PortfolioViewModel>` lambda body into `PortfolioViewModelFactory.Create()`.
3. Replace the registration with `services.AddSingleton<PortfolioViewModelFactory>()` and `services.AddSingleton<PortfolioViewModel>(sp => sp.GetRequiredService<PortfolioViewModelFactory>().Create())`.
4. Verify app launches; no consumer change needed since `PortfolioViewModel` is still registered.

### Phase 2 — Extract `IPortfolioViewModelFactory` interface

Only do this if a second consumer (test harness or "compare two portfolios" view) materializes. Otherwise YAGNI — leave it concrete.

### Phase 3 — Test harness consolidation

1. Create `Assetra.Tests/WPF/Fixtures/PortfolioViewModelTestFactory.cs` mirroring the production factory but with `Mock<>` of every dependency.
2. Migrate `PortfolioViewModelTests` constructor block into the factory.
3. Convert tests one by one: replace inline ctor with `var fx = new PortfolioViewModelTestFactory(); … var vm = fx.Build();`.
4. Existing 109 tests must still pass after migration.

### Phase 4 — Audit consumer wiring

Audit other VMs that take `PortfolioViewModel` directly (AllocationViewModel, DashboardViewModel, FinancialOverviewViewModel). Decide whether to:
- Keep the direct dependency (current behavior).
- Switch consumers to `IPortfolioViewModelFactory.Create()` if/when a multi-instance use case arrives.

For now Phase 4 is documentation only — don't pre-emptively change the ownership model.

---

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| DI-singleton lifecycle changes accidentally | Phase 1 keeps the `AddSingleton` registration; only the construction *recipe* moves. Same instance is returned to all consumers. |
| Test harness drift | Production factory + test factory should share the `PortfolioServices`/`PortfolioUiServices` constructor signatures. If a new dep is added to production, `PortfolioServices` ctor breaks the test factory at compile time — drift becomes a build error, not a runtime mystery. |
| Initialization order edge cases | `AllocationViewModel`/`DashboardViewModel` resolve `PortfolioViewModel` lazily (Singleton). Phase 1 changes nothing about that. |

---

## Acceptance Criteria

1. `PortfolioServiceCollectionExtensions.AddPortfolioContext` no longer contains the 50-line `new PortfolioViewModel(…)` literal.
2. `PortfolioViewModelFactory.Create()` builds the same instance shape the DI block did.
3. App launches and Portfolio page works as before.
4. Existing 109 `PortfolioViewModelTests` pass; test fixture reduced to `var fx = new PortfolioViewModelTestFactory(); var vm = fx.Build();` shape (≤ 5 lines per test).
5. New tests added for the factory itself (smoke test: `Create()` doesn't throw with all real dependencies wired against in-memory SQLite).
