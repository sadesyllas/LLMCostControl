# REVIEW (round 2) — Milestones M20–M24

> Reviewer: Claude (Opus 4.8) · Date: 2026-07-02 · Branch: `with-gemini`
> Re-review after the coder addressed round-1 `REVIEW.md` + implemented M24.

## Verification status

- **Build:** `dotnet build -c Debug` → **0 warnings / 0 errors** (13 projects).
- **Offline tests (run here):** `Domain` **87** ✅ · `Grains` **37** ✅ ·
  `Observability` **9** ✅ — **133 passed, 0 skipped**.
- **Postgres/Testcontainers suites NOT run here** (Docker Hub firewalled; needs
  WSL): `Infrastructure.Tests`, `Admin.App.Tests`, `Tracker.Api.Tests`.
- Every finding below verified by reading the cited `file:line`; the two High
  items were additionally confirmed by grep (`Weekly` absent in `Tracker.Api.Tests`;
  `Deactivat*` absent across `tests/`).

**Overall:** big improvement. Nearly all round-1 findings are genuinely fixed and
the new M24 ledger-projection code is **correct** (append-first, period-scoped
reconstruction, no double-count-on-activation, only-configured-dimensions — all
verified). What still blocks a clean pass is **test coverage** at the HTTP/contract
and cross-activation layers (twice now, `Tested? [x]` outruns the actual tests),
plus one Medium spec-conformance gap (M23 staleness cadence) and a few Lows.

## Passing review ✅

- **M20** — clean. M20-1/2/3 all fixed (shared `GetTopLevelTypeNames` helper called
  by both the scan and the fixture; delegates counted; `Migrations/` exemption
  narrowed to `*.Designer.cs`/`*ModelSnapshot.cs`). Guard is real and green.
- **M22** — all findings fixed (DI wiring via `AddPricingRefresh`; canonical
  `ProviderConverter` persistence; cadence keys canonical; numeric-provider parse
  rejected). Two **Low** notes below (M22-R2a, DI test) — do not block.

## Closed since round 1

X1 (snapshot regenerated, `PendingModelChangesWarning` suppression removed),
M20-1/2/3, M22-1/2/3/4, M23-1 (event now carries `ModelVersionIds`, subscriber
`RefreshAsync` reloads to the new version), M23-2 (lossy `$0` back-fill replaced by
a `RAISE EXCEPTION` guard on non-empty tables). M21-2 & M21-3 were superseded by
M24 (implemented, code correct).

---

## Still open

### M21 — multi-period test coverage (grain layer done; boundary/repo/admin gaps)

The grain + domain weekly tests are now solid (dual accrual + two child rows,
weekly-resets-not-monthly rollover, weekly-exhausted deny, `budget_period`
telemetry tag). Remaining gaps in the milestone's own test plan:

- **M21-R2a — [High] No per-period coverage at the HTTP/contract boundary.**
  `tests/LLMCostControl.Tracker.Api.Tests` contains **zero** `Weekly` (verified);
  `CheckCaptureEndpointTests` / `ContractTests` seed a single Monthly budget and
  assert `.First(b => b.Period == "Monthly")`. No test asserts a 2-entry `budgets`
  array over HTTP, nor a **weekly-driven `allowed:false`**. MILESTONES M21 requires
  "API/contract: per-period response shape … deny when any fails." (Underlying
  behavior is grain-tested, which de-risks it, but the boundary + contract-lock is
  unproven.) *Fix:* add an endpoint + contract test seeding Monthly+Weekly and
  asserting both entries and a weekly-driven deny.
- **M21-R2b — [Med] Multi-dimensional audit only tested against the in-memory
  stub.** `UsageEventRepositoryTests` seeds Monthly-only accruals; no Testcontainers
  test appends a Monthly+Weekly pair and reconstructs both dimensions, so the EF
  mapping/query for multi-period accruals over real Postgres is unverified. *Fix:*
  add a repo test appending both and reconstructing each.
- **M21-R2c — [Med] Period-type filtering in resolution untested with a mixed
  dataset.** `EffectiveBudget.Resolve` does not filter by type; the filter lives in
  `BudgetResolutionRepository`, which `BudgetResolutionTests` exercises Monthly-only.
  No test proves a caller with both a Monthly and a Weekly budget resolves Weekly to
  the weekly amount (not the monthly). *Fix:* resolution test over a mixed dataset.
- **M21-R2d — [Med] Only one direction of rollover and AND-deny is tested.** The
  converse cases (month rolls but not week; monthly exhausted while weekly has
  remaining → deny) are absent — needed to prove the dimensions are truly
  independent. *Fix:* add the two converse tests.
- **M21-R2e — [Low] Binding-period telemetry never exercised with competing
  periods** (`TelemetryTaggingTests` uses a single Monthly budget, so `budget_period`
  is always `Monthly`/`None`; the least-headroom/exhausted selection is never
  observed picking `Weekly`).
- **M21-R2f — [Low] Admin weekly CRUD uncovered** (`GroupCrudTests`/`OverrideCrudTests`
  use `BudgetPeriodType.Monthly` only; MILESTONES M21 asks for set weekly+monthly on
  a group and per-period override validation).

### M23 — pricing versioning

- **M23-R2a — [Med] Cadence-based staleness is not wired into the grain read
  path.** `ModelPricingRepository.ApplyDynamicStaleness` (`:36`) uses
  `_options?.GetCadence(provider) ?? TimeSpan.FromHours(1)`, but the grain path
  builds the repo **without** options — `PricingStore.cs:29`
  (`new ModelPricingRepository(context)`) — so `_options` is null and staleness
  falls back to the hardcoded **1h** for every grain read, regardless of the
  provider's configured cadence (§8.7). The DI-registered `ModelPricingRepository`
  (which would get options) is not used by this path. *Fix:* flow
  `PricingRefreshOptions` into `PricingStore`/the repo on the grain path.
- **M23-R2b — [Low] Covering index omits the tiebreak.** The latest-version
  selectors now order by `EffectiveFrom` **then `Id`** (`ModelPricingRepository.cs:52,65,…`)
  — deterministic ✅ — but the index is still `(Provider, Model, EffectiveFrom)`
  (`CostTrackerDbContext.cs:159`). §8.7 wants a covering index for the lookup; add
  `Id` to the index.
- **M23-R2c — [Low] The non-empty-table migration guard is untested.** The new
  `RAISE EXCEPTION` in `20260702000000_AddPricingVersioning.cs` has no test
  (all migration tests run against empty schemas).

### M24 — running spend as a ledger-derived projection

Production code is **correct** and faithful to §9.1/§9.3/§9.4 (verified:
append-first at `UserBudgetGrain.cs:198/209-219`; period-scoped reconstruction by
`period_key` in `EnsurePeriodSpendLoadedAsync`; no double-count via the
`SpendLoadedKey` guard; only-configured-dimensions). Gaps:

- **M24-R2a — [High] Cross-activation self-heal and idempotency are UNTESTED.**
  No test deactivates/reactivates a grain (verified: **zero** `Deactivat*` across
  `tests/`). The existing idempotency tests reuse the **same activation** (proving
  only the in-memory `GetByIdAsync` early-return), and the reconstruction test uses
  a **first-ever** activation. So the two guarantees that *motivate* M24 —
  "duplicate `requestId` … cross-activation variant (deactivate between the calls)"
  and "a fresh activation after a capture yields correct spend, no double count" —
  are unproven. A regression reintroducing increment-before-append would pass every
  current test (it only manifests after reactivation). *Fix:* add a test that
  reactivates the grain (TestingHost management / a test-only deactivate) and
  asserts (a) rebuilt spend equals the single ledger sum, and (b) a duplicate
  `requestId` across activation yields one row and no double accrual.
- **M24-R2b — [Low] Repo-level period exclusion untested against Postgres.**
  `UsageEventRepositoryTests` seeds and queries the same period only; the
  reset-at-rollover guarantee rests on the stub replicating the EF predicate.
- **M24-R2c — [Low] Unused Orleans "Default" grain-storage provider.** After M24 no
  grain uses `"Default"` storage (`Program.cs:83`); the M24 criterion said to remove
  it or document it as retained — currently neither. Harmless; cleanup only.

### Cross-cutting / M22 (Low)

- **M22-R2a — [Low] `ProviderConverter` coerces unknown DB values to `OpenAI`.**
  `CostTrackerDbContext.cs` `ParseProvider` returns `default` (= `Provider.OpenAI`,
  enum 0) on an unparseable string, so a corrupt/legacy `provider` value reads back
  as OpenAI instead of failing. Low risk (only canonical strings are ever written),
  but a latent data-integrity hole — throw on unparseable input.
- **[Low/cosmetic]** No test asserts the production DI graph registers
  `PricingRefreshJob` + adapters; and the M21/M22 migrations lack `.Designer.cs`
  (only `InitialCreate`, `UpdateModelPricingIndex`, `AddPricingVersioning` have one).
  Build + migrations are fine.

---

## Triage order

1. **M24-R2a** and **M21-R2a** — the two High test gaps; close them so `Tested?` is
   honest for the milestones whose defining behavior they cover.
2. **M23-R2a** (staleness cadence not wired) — the one Medium spec-conformance bug
   in production code.
3. **M21-R2b/c/d** — Medium multi-period test coverage.
4. Remaining Lows (M21-R2e/f, M23-R2b/c, M24-R2b/c, M22-R2a) as capacity allows.

**Reviewed:** M20 ✅ and M22 ✅ pass. **M21, M23, M24** remain un-`Reviewed` until
the items above are closed or explicitly risk-accepted. Delete this file once they
are. No correctness regressions were found in the round-1 fixes.
