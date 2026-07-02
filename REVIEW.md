# REVIEW (round 3) — M21 / M23 / M24 re-review

> Reviewer: Claude (Opus 4.8) · Date: 2026-07-02 · Branch: `with-gemini`
> Re-review of fix commit `d9ece9c` ("Address all Round 2 review findings").

## Verification status

- **Build:** `dotnet build -c Debug` → **0 warnings / 0 errors**.
- **Offline tests (run here):** `Domain` **87** ✅ · `Grains` **41** ✅ (incl. the new
  M24 cross-activation + M21 converse-rollover/deny tests — they **execute and
  pass**) · `Observability` **9** ✅ — **137 passed, 0 skipped**.
- **`Tracker.Api.Tests` — NOT executable in this sandbox.** Attempted; all fail
  uniformly (incl. pre-existing auth tests) at auth-host bootstrap
  (`RequireHttpsMetadata`/mock-OIDC HTTP listener). This is an **environment**
  limitation (same WSL-only bucket as the Testcontainers suites), **not** a
  regression. The M21 endpoint/contract/telemetry weekly tests are therefore
  **code-verified** (assertions read) but not run here. `Infrastructure.Tests`
  and `Admin.App.Tests` likewise need WSL.

## Reviewed — PASS ✅

- **M20**, **M22** — pass (unchanged from round 2; M22's Lows closed:
  `ParseProvider` now throws on unparseable input — `CostTrackerDbContext.cs:258-265`).
- **M21** — **PASS.** The two blocking gaps are closed with load-bearing
  assertions: per-period **endpoint + contract** coverage incl. a weekly-driven
  `allowed:false` (`CheckCaptureEndpointTests.cs`, `ContractTests.cs` now assert
  `Budgets.HaveCount(2)` + the full weekly entry shape); **converse** rollover and
  AND-deny (`UserBudgetGrainTests.cs` — monthly-resets-not-weekly; monthly-exhausted
  deny — these run and pass here); **binding-period telemetry**
  (`TelemetryTaggingTests.cs` asserts `budget_period=="Weekly"` on the least-headroom
  path). Remaining items are low-risk **test-rigor** only (below) — the behavior is
  independently proven at the grain layer (executed) and the HTTP layer (code).
- **M24** — **PASS.** Cross-activation self-heal + idempotency are genuinely tested
  against a **real reactivation** (a shared singleton ledger survives while the
  grain's in-memory fields reset; `UserBudgetGrainTests.cs:589-591` asserts rebuilt
  spend = single ledger sum, and `:625-627` asserts a duplicate `requestId` across
  deactivation does not double-accrue) — and these **pass in the offline Grains
  suite**. Unused Orleans `"Default"` storage is now documented (`Program.cs:83,98`).
  Two Low notes below.

## Still open — M23 (blocking)

- **M23-R2a — [Med] The staleness-cadence fix is wired but INEFFECTIVE in
  production.** `PricingStore` now injects `IOptions<PricingRefreshOptions>`
  (`PricingStore.cs:25`) and forwards it to the repo (`:37`), and
  `ModelPricingRepository.cs:36` uses `_options?.GetCadence(...)`. **But** the DI
  registration only does `services.AddSingleton(options)`
  (`Tracker.Api/DependencyInjection/PricingServiceCollectionExtensions.cs:27-29`)
  with **no** `Configure<PricingRefreshOptions>` / `AddOptions<>()`. So the container
  resolves `IOptions<PricingRefreshOptions>` to a **framework default** (empty
  `ProviderCadences`), `PricingStore._options` gets that empty instance, and
  `GetCadence` returns the **1h fallback for every provider** — the grain read path
  still ignores the configured per-provider cadence (§8.7). The finding is not
  effectively resolved.
  *Fix (either):* inject the **plain** `PricingRefreshOptions` singleton (already
  registered at `:29`) into `PricingStore` instead of `IOptions<>`; **or** register
  `services.Configure<PricingRefreshOptions>(section)` /
  `AddOptions<PricingRefreshOptions>().Bind(section)` (mirroring the correct
  `BudgetGrainOptions` pattern at `Program.cs:39-40`). Then add a test that a
  non-1h configured cadence drives `IsStale`/`StaleSince`.
- **M23-R2c — [Low] The non-empty-table migration guard is still untested.** The
  `RAISE EXCEPTION` in `20260702000000_AddPricingVersioning.cs:59-66` has no test;
  every migration test runs against an empty schema.

## Non-blocking recommendations (test-rigor / cleanup — do not block review)

Production code for these is verified correct; the tests just don't fully prove
what the round-2 findings asked:

- **M21-R2b — [Low]** the Postgres audit test
  (`UsageEventRepositoryTests.cs:98-163`) doesn't assert the round-tripped
  `PeriodKey`/`EffectiveGroupId`/`RunningSpendAfter`, and with a single event both
  period queries return the same row — the period filter isn't adversarially
  exercised. Assert the field values and add a second event under a different period
  key.
- **M21-R2c — [Low]** the mixed-dataset resolution test
  (`BudgetResolutionTests.cs:91-125`) seeds per-user **overrides** for both periods,
  so `ResolveAsync` short-circuits on the override branch
  (`BudgetResolutionRepository.cs:33-35`) and never reaches the **group-budget**
  `PeriodType` filter (`:48-49`) it was meant to exercise (the seeded group budgets
  are dead data). Seed group budgets **only** (no overrides) for both periods.
- **M21-R2f — [Low]** weekly admin CRUD is covered, but the validation cases the
  finding asked for (negative rejected, zero accepted) remain happy-path only.
- **M24-R2b — [Low]** DB-level period-key exclusion is still unproven (single
  event/period). Seed two period keys and assert only the queried one returns.
- **[Low, design smell]** `DeactivateOnIdleAsync()` is a test-only hook exposed on
  the **production** `IUserBudgetGrain` interface (`IUserBudgetGrain.cs:24-28`).
  It works, but any client can now force-deactivate a budget grain; prefer a test
  seam (`IManagementGrain` / internal) over a public-contract method. (Also, the
  test doesn't *assert* reactivation occurred — the invariant holds either way — so
  it's slightly weaker than it looks, but acceptable.)

## Bottom line

Strong round. **M20, M21, M22, M24 pass review.** The only milestone still open is
**M23**, for one real (Med) production defect — the staleness-cadence fix is a no-op
because `IOptions<PricingRefreshOptions>` is never configured — plus a Low untested
migration guard. Fix M23-R2a (a 1-line DI change) and add its test, and M23 passes;
the non-blocking test-rigor items above can be tightened opportunistically. Delete
this file once M23 is closed.

**Reviewed:** M20 ✅ · M21 ✅ · M22 ✅ · M24 ✅ · **M23 ✗ (pending R2a).**
No functional regressions were introduced by the round-2 fixes.
