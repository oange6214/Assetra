# Assetra Complete Goals Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the current financial goals page from a goal CRUD list into a complete planning module with transparent progress sources, milestones, funding rules, planning helpers, FIRE integration, and dashboard visibility.

**Architecture:** Keep goal state in `Assetra.Core.Models`, calculation/query behavior in `Assetra.Application.Goals`, persistence in `Assetra.Infrastructure.Persistence`, and WPF orchestration in `Assetra.WPF.Features.Goals`. Prefer surfacing existing goal domain objects (`GoalMilestone`, `GoalFundingRule`, `GoalPlanningService`, `GoalProgressQueryService`) before adding new model types.

**Tech Stack:** .NET 10, WPF MVVM, CommunityToolkit.Mvvm, SQLite repositories, xUnit, Moq.

---

## Current State

Assetra already has a solid goal domain foundation:

- `Assetra.Core/Models/FinancialGoal.cs`
  - Goal name, target amount, current amount, deadline, notes, linked asset class, and optional portfolio group id.
- `Assetra.Core/Models/GoalMilestone.cs`
  - Milestone model already exists.
- `Assetra.Core/Models/GoalFundingRule.cs`
  - Funding rule model already exists.
- `Assetra.Application/Goals/GoalPlanningService.cs`
  - Planning calculation service already exists.
- `Assetra.Application/Goals/GoalProgressQueryService.cs`
  - Progress query service already exists.
- `Assetra.Infrastructure/Persistence/GoalSqliteRepository.cs`
  - Financial goal persistence.
- `Assetra.Infrastructure/Persistence/GoalMilestoneSqliteRepository.cs`
  - Milestone persistence.
- `Assetra.Infrastructure/Persistence/GoalFundingRuleSqliteRepository.cs`
  - Funding rule persistence.
- `Assetra.WPF/Features/Goals/GoalsViewModel.cs`
  - WPF CRUD, FIRE sync message handling, and summary state.
- `Assetra.WPF/Features/Goals/GoalsView.xaml`
  - Current list/card UI.
- `Assetra.WPF/Features/Goals/GoalsDialogsHost.xaml`
  - Add/edit/delete dialogs.

The missing piece is productization. The WPF page currently exposes goal CRUD and simple progress cards, but it does not yet expose milestones, funding rules, planning recommendations, detailed progress source explanations, or a goal detail workflow.

---

## Product Definition

### What "Complete Goals" Means In Assetra

A complete financial goals module should answer these user questions:

1. What goals am I working toward?
2. How far am I from each goal?
3. Is this goal manually tracked or automatically linked to app data?
4. If it is automatic, which source is driving progress?
5. How much should I contribute monthly to reach the goal by deadline?
6. What milestones have I reached?
7. Which funding rules are supposed to move money toward this goal?
8. Which goals came from FIRE, and what calculation produced them?
9. What should I do next?

### UX Positioning

Goals should behave like a planning surface, not only a record table.

- The main page shows all goals, summary state, and clear calls to action.
- Clicking a goal opens a detail drawer.
- The detail drawer is where users manage milestones, funding rules, notes, and planning suggestions.
- The add/edit dialog remains focused on the minimum required goal identity and tracking source.
- Advanced planning details should not overload the list view.

### Goal Types

Use one shared `FinancialGoal` model, but surface distinct user-facing tracking modes:

- **Manual goal**
  - User enters current amount manually.
  - Best for goals that Assetra cannot infer from portfolio or net worth data.
- **Auto-tracked goal**
  - Progress comes from `LinkedAssetClass` such as net worth, cash, investments, real estate, retirement, or physical assets.
- **Portfolio group goal**
  - Progress comes from a selected investment group.
  - This should be treated as auto-tracked because the amount is derived from portfolio holdings.
- **FIRE-synced goal**
  - A goal created or updated by FIRE.
  - The detail drawer should show it came from FIRE and display the formula or scenario name when available.

### Terminology

Use wording that tells users where the number comes from:

- `追蹤方式`
  - Manual: `手動輸入`
  - Linked asset class: `自動追蹤：{資產類別}`
  - Portfolio group: `自動追蹤：投資群組 {群組名稱}`
  - FIRE: `FIRE 同步`
- `建議每月投入`
  - Planning helper output.
- `里程碑`
  - Target checkpoints.
- `撥款規則`
  - Scheduled or suggested contribution rules.
- `距離目標`
  - Remaining amount.

---

## Scope

### In Scope

- Tighten current goal CRUD and tracking-source display.
- Add a goal detail drawer.
- Surface milestones in UI.
- Surface funding rules in UI.
- Add planning helper outputs.
- Keep FIRE sync understandable and visible.
- Improve dashboard widget coherence.
- Add tests that cover the actual user flows.
- Update docs when implementation is done.

### Out Of Scope For This Plan

- Bank automation that actually moves money.
- Cloud sync redesign.
- AI goal recommendation.
- Mobile/PWA goal screens.
- Calendar scheduling UI beyond funding-rule display.

---

## File Map

### Core

- Modify: `Assetra.Core/Models/FinancialGoal.cs`
  - Ensure auto-tracking semantics include portfolio group tracking.
  - Keep backward compatibility for existing rows.
- Keep: `Assetra.Core/Models/GoalMilestone.cs`
  - Use existing milestone model unless a UI requirement proves a missing field.
- Keep: `Assetra.Core/Models/GoalFundingRule.cs`
  - Use existing funding rule model unless recurrence/account fields are insufficient.
- Keep: `Assetra.Core/Models/GoalProgress.cs`
  - Use as the query result for list/detail progress display.

### Application

- Modify: `Assetra.Application/Goals/GoalProgressQueryService.cs`
  - Ensure progress source is explicit enough for WPF to display.
  - Ensure portfolio group progress is included when `PortfolioGroupId` is set.
- Modify: `Assetra.Application/Goals/GoalPlanningService.cs`
  - Expose required monthly contribution, projected completion date, and gap.
- Keep: `Assetra.Application/Goals/GroupBalanceQueryService.cs`
  - Use for portfolio group progress where applicable.

### Infrastructure

- Keep: `Assetra.Infrastructure/Persistence/GoalSqliteRepository.cs`
  - Existing goal persistence.
- Keep: `Assetra.Infrastructure/Persistence/GoalMilestoneSqliteRepository.cs`
  - Existing milestone persistence.
- Keep: `Assetra.Infrastructure/Persistence/GoalFundingRuleSqliteRepository.cs`
  - Existing funding rule persistence.
- Modify only if tests prove schema or repository behavior is missing.

### WPF

- Modify: `Assetra.WPF/Features/Goals/GoalsViewModel.cs`
  - Add selected goal detail state.
  - Load milestones and funding rules for the selected goal.
  - Add planning helper view state.
  - Add commands for milestone and funding-rule CRUD.
  - Add explicit source badges and FIRE source state.
- Modify: `Assetra.WPF/Features/Goals/GoalRowViewModel.cs`
  - Display tracking source, remaining amount, deadline status, next action, and source badge.
- Modify: `Assetra.WPF/Features/Goals/GoalsView.xaml`
  - Replace the page as a list + detail drawer workflow.
  - Keep empty state centered.
  - Avoid page-local styles; use DesignSystem resources.
- Modify: `Assetra.WPF/Features/Goals/GoalsDialogsHost.xaml`
  - Keep add/edit dialog lean.
  - Add milestone and funding-rule dialogs if drawer inline editing becomes crowded.
- Modify: `Assetra.WPF/Features/FinancialOverview/Widgets/GoalsWidget.xaml`
  - Show goal count, nearest deadline, and at-risk/behind goals.
- Modify localization:
  - `Assetra.WPF/Languages/zh-TW.xaml`
  - `Assetra.WPF/Languages/en-US.xaml`

### Tests

- Modify: `Assetra.Tests/WPF/GoalsViewModelTests.cs`
  - Add WPF workflow coverage.
- Modify or add:
  - `Assetra.Tests/Application/Goals/GoalProgressQueryServiceTests.cs`
  - `Assetra.Tests/Application/Goals/GoalPlanningServiceTests.cs`
  - `Assetra.Tests/Infrastructure/GoalAuxiliaryRepositoryTests.cs`

### Docs

- Modify after implementation:
  - `docs/INDEX.md`
  - `docs/guides/Goals-Planning.md`
  - `docs/releases/CHANGELOG.md`

---

## Implementation Plan

### Phase 1: Current MVP Hardening

**Intent:** Make the current goal list correct before adding new UI.

- [x] Add tests proving `FinancialGoal.IsAutoTracked` treats `PortfolioGroupId` as auto-tracked when a portfolio group drives progress.
- [x] Update `FinancialGoal.IsAutoTracked` if it currently only checks `LinkedAssetClass`.
- [x] Add WPF tests for add/edit/delete preserving name, target amount, current amount, deadline, notes, linked asset class, and portfolio group.
- [x] Add WPF test for FIRE-saved message before first load so the page still performs a full load later.
- [x] Add WPF test that invalid current amount cannot be saved as `0`.
- [x] Add source badge fields to `GoalRowViewModel`.
- [x] Show `手動`, `自動追蹤`, `投資群組`, or `FIRE 同步` in each goal card.
- [x] Ensure list refresh updates goal cards after edit/delete and after FIRE sync.
- [x] Run `dotnet test Assetra.slnx --filter Goals`.

### Phase 2: Goal Detail Drawer

**Intent:** Move rich goal management out of the list card.

- [x] Add `SelectedGoal` state to `GoalsViewModel`.
- [x] Add `OpenGoalDetailCommand` and `CloseGoalDetailCommand`.
- [x] Add detail drawer shell to `GoalsView.xaml`.
- [x] Detail header shows goal name, target, current/progress, deadline, tracking source, and source explanation.
- [x] Detail overview shows:
  - Current amount.
  - Target amount.
  - Remaining amount.
  - Progress percent.
  - Deadline status.
  - Next recommended action.
- [x] Detail drawer actions:
  - [x] Edit goal.
  - [x] Add milestone.
  - [x] Add funding rule.
  - [x] Delete goal.
- [x] Add WPF tests proving selecting a goal loads drawer state and closing clears it.

### Phase 3: Milestones

**Intent:** Let users break a goal into meaningful checkpoints.

- [x] Load `GoalMilestone` rows for the selected goal.
- [x] Display milestones as a compact checkpoint list in the detail drawer.
- [x] Add milestone command creates a milestone with:
  - Name.
  - Target amount.
  - Target date.
- [x] Edit milestone command updates the selected milestone.
- [x] Delete milestone command removes the selected milestone.
- [x] Completed state is derived from current goal progress unless the existing model already stores manual completion.
- [x] Add tests for milestone CRUD repository behavior if not already covered.
- [x] Add WPF tests proving selected goal detail loads milestones.
- [x] Add WPF test for adding a milestone from the selected goal detail drawer.
- [x] Add WPF tests for editing/deleting milestone commands.

### Phase 4: Funding Rules

**Intent:** Show how the user intends to fund the goal.

- [x] Load `GoalFundingRule` rows for the selected goal.
- [x] Display funding rules in the detail drawer with amount, cadence, enabled state, and date range.
- [x] Add source account labels and next expected contribution to funding-rule rows.
- [x] Add funding rule command creates a rule with:
  - Amount.
  - Cadence.
  - Date range.
  - Enabled state from the existing model default.
- [x] Edit funding rule command updates amount/cadence/date fields while preserving source/enabled state.
- [x] Delete or disable funding rule command removes or disables it according to existing repository support.
- [x] Funding rules do not automatically create transactions in this phase.
- [x] Add WPF tests proving selected goal detail loads funding rules.
- [x] Add WPF test proving a funding rule can be added through the drawer and refreshes planning.
- [x] Add WPF tests proving funding rules can be edited/deleted or disabled through the drawer.

### Phase 5: Planning Helper

**Intent:** Make goals actionable, not just descriptive.

- [x] Use `GoalPlanningService` to calculate required monthly contribution.
- [x] Display:
  - Required monthly contribution to reach target by deadline.
  - Estimated completion at current funding rate.
  - Gap between current funding and required funding.
  - Warning when no deadline exists.
  - Warning when target amount is already reached.
- [x] For manual goals, use current amount from the goal.
- [x] For auto-tracked goals, use the progress query amount.
- [x] For funding-rate calculation, prefer active funding rules.
- [x] If no funding rules exist, show a prompt and inline form to create one.
- [x] Add tests for planning helper edge cases:
  - [x] Already completed goal.
  - [x] No deadline.
  - [x] Deadline in the past.
  - [x] Zero target.
  - [x] Active funding rule below required amount.

### Phase 6: FIRE And Dashboard Integration

**Intent:** Make FIRE-created goals understandable in the Goals page.

- [x] Mark FIRE-synced goals visibly.
- [x] Show FIRE source text in detail drawer:
  - FIRE required assets.
  - Scenario name if available.
  - Last synced time if available.
- [x] When FIRE sync updates a goal, the Goals page refreshes without hiding other saved goals.
- [x] Update `GoalsWidget.xaml` to show:
  - Total active goals.
  - Nearest deadline.
  - Goals behind schedule.
  - Top goal progress.
- [x] Add tests that FIRE sync does not replace the entire goals list with only the FIRE goal.

### Phase 7: UX And DesignSystem Pass

**Intent:** Make the feature visually consistent with the rest of Assetra.

- [x] Goal cards use shared card spacing and typography.
- [x] Drawer uses the same modal/drawer language as portfolio asset detail.
- [x] Buttons use shared primary/secondary/destructive styles.
- [x] Empty state is centered horizontally and vertically when there are no goals.
- [x] Forms use consistent label/input/helper/error rhythm.
- [x] No page-local button/input/tab styles are introduced.
- [x] Keyboard behavior:
  - Tab order follows visual order through the Goals list, detail drawer actions, and add/edit form fields.
  - Escape closes the add/edit modal, delete confirmation, and detail drawer where applicable.
  - Enter triggers the primary add/edit form action where applicable.

### Phase 8: Verification And Docs

**Intent:** Finish with tests and docs that explain the module.

- [x] Run `dotnet build Assetra.sln`.
  - 2026-06-03: `dotnet build Assetra.slnx --no-restore` passed with 0 warnings and 0 errors.
  - 2026-06-03: `dotnet build Assetra.slnx --no-restore -p:UseSharedCompilation=false` passed with 0 warnings and 0 errors after keyboard/drawer polish.
- [x] Run `dotnet test Assetra.sln --filter Goals`.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "Goals" -p:OutDir=".tmp-test-bin/"` passed 50/50.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "Goals" -p:OutDir=".tmp-test-bin/"` passed 54/54 after CRUD/FIRE sync regression tests.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "Goals" -p:OutDir=".tmp-test-bin/"` passed 58/58 after milestone/funding-rule edit/delete workflow tests.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "Goals" -p:OutDir=".tmp-test-bin/"` passed 59/59 after detail overview resolved-progress and next-action tests.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "Goals" -p:OutDir=".tmp-test-bin/"` passed 60/60 after milestone completion derived from current progress.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "Goals" -p:OutDir=".tmp-test-bin/"` passed 61/61 after funding-rule source and next-contribution row display.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~GoalsWidgetSummary_ProjectsGoalRiskAndTopProgress|FullyQualifiedName~OpenGoalDetailCommand_ShowsFireSourceDetails|FullyQualifiedName~TrackingSourceLabel_ShowsFire" -p:OutDir=".tmp-test-bin/"` passed 3/3 after FIRE detail source and dashboard widget summary.
- [x] Run full WPF-related tests if Goals touches shared UI commands or dialogs.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Goals|FullyQualifiedName~FinancialOverview" -p:OutDir=".tmp-test-bin/"` passed 76/76.
  - 2026-06-03: `dotnet test Assetra.Tests/Assetra.Tests.csproj --filter "FullyQualifiedName~Goals|FullyQualifiedName~FinancialOverview" -p:OutDir=".tmp-test-bin/" -p:UseSharedCompilation=false` passed 76/76 after keyboard/drawer polish.
- [x] Add `docs/guides/Goals-Planning.md`.
- [x] Update `docs/INDEX.md`.
- [x] Update `docs/releases/CHANGELOG.md` after implementation.
- [x] Add screenshots or manual QA notes for:
  - Empty goals.
  - Manual goal detail.
  - Auto-tracked goal detail.
  - FIRE-synced goal detail.
  - Goal with milestones.
  - Goal with funding rules.
  - 2026-06-03: Manual QA notes are captured in `docs/guides/Goals-Planning.md`; screenshot capture was not performed in this pass.

Manual QA notes:

- Empty goals: page shows a centered empty state and keeps the primary Add goal action visible.
- Manual goal detail: source is shown as quiet inline text, not a competing badge; current, target, remaining, deadline, progress, and next action are visible in the drawer.
- Auto-tracked goal detail: drawer explains the linked asset class or portfolio group and still exposes edit/delete actions.
- FIRE-synced goal detail: drawer shows FIRE source, required assets, source scenario, and last sync information.
- Goal with milestones: milestones appear in the detail drawer with completion derived from current progress.
- Goal with funding rules: funding rules show source, frequency, amount, and next contribution information.

---

## Acceptance Rules

A migrated Goals implementation is done only when all of these are true:

- [x] A user can create, edit, and delete goals without losing existing goals.
- [x] A user can clearly tell whether a goal is manual, auto-tracked, portfolio-group tracked, or FIRE-synced.
- [x] A user can open a goal detail drawer and understand current progress, remaining amount, deadline, and next action.
- [x] A user can create, edit, and delete milestones.
- [x] A user can create, edit, and disable/delete funding rules.
- [x] Planning helper explains the monthly contribution needed to reach the goal.
- [x] FIRE sync does not cause first-load or partial-load issues.
- [x] Financial overview widget reflects the same goal state as the Goals page.
- [x] All goal workflows are covered by targeted tests.
- [x] The UI follows Assetra DesignSystem patterns and does not introduce page-local style drift.

---

## Suggested Commit Slices

1. `fix(goals): harden tracking source and current goal tests`
2. `feat(goals): add goal detail drawer`
3. `feat(goals): surface milestones`
4. `feat(goals): surface funding rules`
5. `feat(goals): add planning helper outputs`
6. `feat(goals): clarify FIRE sync and overview widget`
7. `docs(goals): document complete goals workflow`
