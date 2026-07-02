# REVIEW — Milestones M20–M23

> Reviewer: Claude (Opus 4.8) · Date: 2026-07-02 · Branch: `with-gemini`
> Scope: M20–M23 (iteration-2 scope). Reviewed against `SPEC.md` §6.2, §7, §8.2,
> §8.5, §8.7, §9.4, §10.2, §17 and each milestone's acceptance criteria.

## Verification status

- **Build:** `dotnet build -c Debug` → **0 warnings / 0 errors** across all 13 projects (verified).
- **Offline tests (run here):** `Domain.Tests` 83 ✅ · `Grains.Tests` 33 ✅ ·
  `Observability.Tests` 9 ✅ — **125 passed, 0 skipped**.
- **Postgres/Testcontainers suites NOT run here** (Docker Hub firewalled on the
  Windows host; needs WSL per `TODO.md`): `Infrastructure.Tests`,
  `Admin.App.Tests`, `Tracker.Api.Tests`. Their green status rests on the coder's
  WSL run and was **not** independently reproduced in this review.
- Every finding below was verified by reading the cited `file:line`.

Overall: the production code is mostly correct and the hardest algorithms (ISO-week
math, insert-on-change equality, per-period AND-deny logic) are right. The problems
are concentrated in **test coverage, EF migration/snapshot hygiene, and a few
spec-contract gaps**. Findings must be addressed (or explicitly risk-accepted)
before these milestones are `Reviewed`.

---

## Cross-cutting (affects M21 & M23)

### X1 — [High] EF model snapshot is stale; `PendingModelChangesWarning` is suppressed to hide the drift
- **Where:** `src/LLMCostControl.Infrastructure/Data/Migrations/CostTrackerDbContextModelSnapshot.cs`
  (contains **zero** occurrences of `effective_from`, `pricing_version_id`,
  `usage_event_period_accruals`, `period_type`, `WeeklyRunningSpend` — i.e. it
  still describes the pre-M21 schema); suppressed at
  `tests/LLMCostControl.Infrastructure.Tests/RepositoryTestBase.cs:33` and
  `tests/LLMCostControl.Admin.App.Tests/PostgresFixture.cs:29`.
- **Problem:** The M21/M22/M23 migrations are hand-written and apply correctly at
  runtime (`MigrateAsync` replays migration files), which is why tests pass — but
  only because both fixtures `Ignore(PendingModelChangesWarning)`. The snapshot is
  the source of truth for the **next** `dotnet ef migrations add`, which will emit
  a migration that re-drops/re-adds `unit_prices`, `StaleSince`, the period
  columns, the accruals table, etc. against an already-migrated DB — a data-loss
  trap. This violates the "migration applies cleanly" criterion (M21 #2/#6, M23 #5)
  in spirit and masks real model drift. `TODO.md` already notes `dotnet-ef` wasn't
  available in the dev env.
- **Fix:** Regenerate the snapshot (and the `.Designer.cs` files for the three new
  migrations) with real `dotnet ef` tooling under WSL so it matches
  `OnModelCreating`, then **remove** the `PendingModelChangesWarning` suppression
  from both fixtures. If the suppression must stay temporarily, add a test that
  fails when the model has pending changes, so drift is caught deliberately rather
  than hidden.

---

## M20 — One top-level type per file + guard

Deliverable is sound: real Roslyn scan, all `src/` files correctly split, nested
types excluded, filename-matches-type enforced, docs preserved, build 0/0, guard
test green. Findings are quality/robustness improvements.

### M20-1 — [Medium] The "proof it guards" fixture duplicates the detection logic instead of exercising the production scan
- **Where:** `tests/LLMCostControl.Domain.Tests/CodingStandardTests.cs:94-110`
  (`Test_OneTopLevelTypePerFileStandard_WithFixture`) re-implements the top-level
  query at `:103-106`, which is a copy of the real scan at `:64-67`.
- **Problem:** The negative fixture proves only that *a duplicated snippet* flags a
  2-type file — not that the production scan (`OneTopLevelTypePerFileStandard_ShouldBeEnforced`)
  does. If the real scan regressed (bad ancestor filter, wrong exemption), the
  fixture would still pass, giving false confidence. This undercuts M20's own
  test-plan promise that "a fixture … is detected as a violation (so an accidental
  regression would fail CI)."
- **Fix:** Extract the per-file detection into one shared internal method (e.g.
  `static IReadOnlyList<string> TopLevelTypeNames(SyntaxNode root)`), and have both
  the real scan and the fixture Theory call it.

### M20-2 — [Low] `delegate` top-level types are invisible to the guard
- **Where:** `CodingStandardTests.cs:64-67` counts `BaseTypeDeclarationSyntax`,
  which excludes `DelegateDeclarationSyntax`.
- **Problem:** A top-level `public delegate …` is a real top-level type but isn't
  counted, so a file with a delegate + a class would slip past. No delegates exist
  in `src/` today — latent hole only.
- **Fix:** Also collect top-level `DelegateDeclarationSyntax` toward the per-file
  count and filename-match.

### M20-3 — [Low] `Migrations/` folder exemption is broader than §17
- **Where:** `CodingStandardTests.cs:46` skips any path containing a `Migrations`
  segment.
- **Problem:** §17 exempts only `*.Designer.cs` and the model snapshot — not
  hand-authored migration bodies. Each migration currently declares one type, so
  nothing is hidden today, but the exemption exceeds the spec.
- **Fix:** Narrow the exemption to `*.Designer.cs` + `*ModelSnapshot.cs` and let the
  ordinary rule apply to migration bodies.

---

## M21 — Weekly budget period (multi-period budgeting)

Production code is largely correct — including the ISO-week-year boundary math
(`BudgetPeriod.cs` uses `System.Globalization.ISOWeek`), the per-period AND-deny /
empty-set→`AllowNonBudgetedUsers` logic, independent per-type rollover keys, and the
zero-budget cut-off. The serious gaps are in testing and accrual ordering.

### M21-1 — [High] The defining multi-period behavior is UNTESTED, yet M21 is marked `Tested? [x]`
- **Where:** `tests/LLMCostControl.Grains.Tests/UserBudgetGrainTests.cs` and
  `tests/.../Stubs/StubBudgetStore.cs` contain **zero** `Weekly` occurrences;
  `tests/LLMCostControl.Domain.Tests/EffectiveBudgetTests.cs` passes only
  `BudgetPeriodType.Monthly` in all 15 call sites. The **only** weekly coverage is
  `BudgetPeriodTests.cs` (pure `BudgetPeriod` value-object math).
- **Problem:** None of the behaviors M21's test plan promises
  (`MILESTONES.md:448-463`) are exercised: (a) a single capture accruing to **both**
  weekly + monthly; (b) two `usage_event_period_accruals` child rows per capture
  (the audit test at `UserBudgetGrainTests.cs` asserts a single accrual); (c)
  week-only rollover leaving monthly untouched (rollover tests cross only month
  boundaries); (d) deny when weekly is exhausted but monthly is fine; (e) a
  per-period `budgets` array with ≥2 entries in the check/capture endpoints and
  contract tests (they only read `.First(b => b.Period == "Monthly")`); (f)
  `budget_period`/binding-period telemetry assertion. The `Tested? [x]` mark
  (`MILESTONES.md` table row + `## M21` section) is therefore **not substantiated**
  (rule 4: no untested code advances).
- **Fix:** Add grain tests that seed both `SetBudget(caller, Monthly, …)` and
  `SetBudget(caller, Weekly, …)` (the stub already keys by period type) covering
  dual accrual + two child rows, week-only rollover, and deny-when-weekly-exhausted;
  add a domain test where a weekly override/group wins within the weekly dimension;
  add an endpoint + contract test asserting a 2-entry `budgets` array and a
  weekly-driven deny; add a telemetry test asserting `budget_period`. Only then
  re-mark `Tested`.

### M21-2 — [Medium] Capture persists the accrual to state *before* writing the audit row and ignores `AppendAsync`'s result → double-accrual window
- **Where:** `src/LLMCostControl.Grains/Implementations/UserBudgetGrain.cs`:
  accrue + `WriteStateAsync()` at `:163-176`, audit `AppendAsync(...)` at `:225`
  (return value discarded); idempotency guard is an up-front `GetByIdAsync` at
  `:106-113`.
- **Problem:** §6.2.2/§9.4 require that a duplicate capture "must not
  double-accrue." Running spend is persisted before the audit row exists, so if the
  grain crashes/reactivates between `:176` and `:225` (or the two duplicate calls
  land on different activations), the retry's `GetByIdAsync` finds no event and
  **re-accrues** — spend is counted twice while only one audit row eventually
  exists. The single-activation happy path is safe (and tested), but the ordering
  is the reverse of the safe one.
- **Fix:** Append the audit row **first** (its `EventId` PK is the idempotency key),
  and accrue/persist state only on a successful, non-duplicate append — or wrap
  accrual + append in one transaction. At minimum, honor the `bool` returned by
  `AppendAsync` and skip/rollback the accrual when it indicates a duplicate.

### M21-3 — [Low] Cost accrues to unconfigured period dimensions
- **Where:** `UserBudgetGrain.cs:163-169` updates both `MonthlyRunningSpend` and
  `WeeklyRunningSpend` unconditionally, regardless of `monthly.HasBudget` /
  `weekly.HasBudget`.
- **Problem:** §7 says accrue "to every period type **in the caller's
  effective-budget set**." Invisible today (child rows, response, and gating are all
  gated on `HasBudget`), but if a budget for a previously-unconfigured period type
  is added mid-period, the pre-accrued running spend is immediately counted against
  the new budget until that period rolls over.
- **Fix:** Accrue to a dimension only when its `EffectiveBudget.HasBudget`, or reset
  a dimension's running spend when a budget first appears for it. Add a
  "budget added mid-period on a previously-unbudgeted type" test.

---

## M22 — Additional providers (Azure AI Foundry, Vertex AI)

Provider enum + canonical mapping, explicit-only resolution (native prefixes still
win), grain-key consistency across capture/publish, and the validator are all
correctly done and well-tested (verified). Findings are about wiring and string
representation, several pre-existing but surfaced by M22's own criteria.

### M22-1 — [High] The pricing refresh job and adapters are not registered in production DI — live provider fetch never runs
- **Where:** `src/LLMCostControl.Tracker.Api/Program.cs:102` registers only
  `AddHostedService<PricingStreamSubscriber>()`. There is **no**
  `AddHostedService<PricingRefreshJob>()` and **no** `IPricingAdapter` / `HttpClient`
  registration anywhere in `src/` (confirmed by grep across the tree).
- **Problem:** M22 criterion #2 requires the two new adapters "registered with the
  refresh job," and §8.4 makes the refresh job the sole writer of pricing. As
  shipped, none of the five adapters run in the deployed app — live/hybrid pricing
  fetch (§8.2) never happens; pricing is populated only via the Admin manual upload
  (§8.3). This is a **pre-existing** wiring gap (from the M6/M7 era) that M22's
  criterion makes in-scope.
- **Fix:** Add an `Infrastructure` `IServiceCollection` extension that registers
  `PricingRefreshOptions`, a configured `HttpClient` + source URL per provider, all
  five `IPricingAdapter` implementations, and `AddHostedService<PricingRefreshJob>()`;
  call it from `Program.cs`. Add an integration test asserting the job is a
  registered hosted service and that a fetch persists a new version.

### M22-2 — [Medium] Provider persisted to the DB as the PascalCase enum name, diverging from the canonical wire/grain string
- **Where:** `src/LLMCostControl.Infrastructure/Data/CostTrackerDbContext.cs:142`
  (`model_pricing.Provider`) and `:173` (`usage_events.provider`) use
  `HasConversion<string>()` → EF stores `"AzureFoundry"`/`"VertexAI"`/`"OpenAI"`,
  not the canonical `"azure-foundry"`/`"vertex-ai"`/`"openai"`.
- **Problem:** A second, drifting string representation of provider. Internally
  consistent for EF round-trips (so capture/pricing still work), but the §9.4 audit
  `provider` column and any external/BI query see PascalCase while every other
  surface (API wire, grain key, config, pricing file) uses the canonical form —
  contrary to §8.5's single canonical-string intent. Pre-existing across all
  providers; M22 extends it to the two new ones.
- **Fix:** Apply a value converter that persists via
  `ProviderResolver.ToCanonicalString` / `TryParseProvider` so the DB stores the
  canonical form consistently.

### M22-3 — [Low] Refresh cadence/jitter overrides keyed by `provider.ToString()`, not the canonical string
- **Where:** `src/LLMCostControl.Infrastructure/Pricing/PricingRefreshOptions.cs`
  (`GetCadence`/override lookups keyed on `provider.ToString()` = `"AzureFoundry"`).
- **Problem:** An operator configuring an override with the canonical
  `"azure-foundry"` (as used for `Pricing:ProviderInference`) will silently miss and
  fall back to the default cadence. Config foot-gun; not a correctness break.
- **Fix:** Key on `ProviderResolver.ToCanonicalString(provider)`.

### M22-4 — [Low] `TryParseProvider` accepts numeric enum values
- **Where:** `src/LLMCostControl.Domain/Pricing/ProviderResolver.cs:40`
  (`Enum.TryParse` + `Enum.IsDefined`) resolves `provider:"3"`→`AzureFoundry`, etc.
- **Problem:** Hygiene only — a numeric string mis-resolves to a provider. Fail-closed
  downstream (unseeded `(provider, model)` → `UnknownModelException`), and the file
  import is separately guarded by `KnownProviders`, so no mis-pricing. Pre-existing.
- **Fix:** Reject purely numeric input before `Enum.TryParse`, or match only the
  known canonical name set.

---

## M23 — Pricing version history (insert-on-change) + usage references version

Core mechanics are solid and well-tested: append-only versioned `model_pricing`
(surrogate id + `EffectiveFrom`), correct insert-on-change equality over all six
fields (`ModelPricingRepository.cs:104-113`, nullable cache prices compared
correctly), `UsageEvent` replacing the embedded `unit_prices` with a
`pricing_version_id` FK (`DeleteBehavior.Restrict`), derived staleness (`StaleSince`
is `Ignore`d in EF). Findings below.

### M23-1 — [Medium] `pricing-updated` stream event carries no version id; the subscriber invalidates instead of "refreshing to the new version"
- **Where:** `src/LLMCostControl.Grains.Abstractions/StreamEvents/PricingUpdatedStreamEvent.cs:12-24`
  (only `Provider`, `UpdatedModels` names, `UpdatedAt`); publisher
  `src/LLMCostControl.Grains/Publishers/OrleansPricingPublisher.cs`; subscriber
  `src/LLMCostControl.Grains/.../PricingStreamSubscriber.cs` does `_cache.Remove(key)`.
- **Problem:** §8.7 and M23 criterion #4 say the event "carries the affected
  `(provider, model)` and its **new version id(s)** so activations refresh to the new
  version." The grain *does* cache `PricingVersionId`, but the push carries no id and
  the subscriber only invalidates → the next read re-queries the DB. Convergence is
  functionally preserved (next capture uses the latest version), but the specified
  push-with-version-id contract is not implemented (it degrades to invalidation).
- **Fix:** Add the changed models' new version ids to the event (have
  `InsertNewVersionAsync` report which models actually changed + their new ids so the
  job publishes only those), and have the subscriber update the cache entry to the
  new version rather than blind-removing.

### M23-2 — [Medium] Legacy data-migration seeds a `$0` placeholder version and repoints ALL historical `usage_events` at it
- **Where:** `src/LLMCostControl.Infrastructure/Data/Migrations/20260702000000_AddPricingVersioning.cs:59-73`.
- **Problem:** On a non-empty `usage_events` table the migration inserts one
  `('OpenAI','placeholder-legacy-migration', 0,0,0,0)` version and
  `UPDATE usage_events SET pricing_version_id = '<placeholder>'` for **every**
  historical row, regardless of the real provider/model/prices, then drops the
  `unit_price_*` columns. §9.4 requires the referenced version's prices to
  reconstruct the recorded cost — now false for all pre-migration rows (they claim
  `$0` unit prices while `cost_amount` is non-zero) and non-OpenAI usage is
  mislabelled OpenAI. Harmless on the current empty tables (the `DO` block is guarded
  by `IF EXISTS`), but the code is objectively wrong for any populated deployment.
- **Fix:** Back-fill real versions from the about-to-be-dropped `unit_price_*`
  columns — create a version per distinct historical `(provider, model, prices)`
  tuple and map each usage row to its match before dropping the columns. If empty-only
  is acceptable for this PoC, `RAISE EXCEPTION` when rows exist so the lossy path can
  never run silently.

### M23-3 — [Medium] Derived staleness uses a hardcoded 1-hour threshold instead of the provider's configured cadence
- **Where:** `src/LLMCostControl.Infrastructure/Repositories/ModelPricingRepository.cs:30-32`
  (`DateTimeOffset.UtcNow - pricing.FetchedAt > TimeSpan.FromHours(1)`).
- **Problem:** §8.7 says staleness is "computed at read time from the latest
  version's `fetchedAt` and the **provider's configured refresh cadence**." A provider
  with a 6-hour cadence is wrongly flagged stale after 1h; a 15-minute cadence isn't
  flagged until 1h. The repository has no access to `PricingRefreshOptions`, so the
  cadence isn't even available at the read site.
- **Fix:** Provide the per-provider cadence (from `PricingRefreshOptions`) to the
  staleness computation instead of the literal `TimeSpan.FromHours(1)`.

### M23-4 — [Low] "Current version" is non-deterministic when two versions share `EffectiveFrom`
- **Where:** `ModelPricingRepository.cs:43,55` (`OrderByDescending(EffectiveFrom)
  .FirstOrDefault`), `:63-67,80-84` (client-side `GroupBy` after `ToListAsync`); index
  `CostTrackerDbContext.cs:156` is not unique and has no tiebreak column;
  `InsertNewVersionAsync` sets `EffectiveFrom = UtcNow` (`:122`).
- **Problem:** Two versions inserted in the same tick (admin upload then fetch, or two
  writers) get identical `EffectiveFrom`; `OrderByDescending(...).First()` then returns
  an arbitrary row, so "current" — and the `pricing_version_id` newly captured usage
  points at — is non-deterministic. §8.7 requires a deterministic "greatest
  `EffectiveFrom`".
- **Fix:** Add a deterministic tiebreak (`.ThenByDescending(p => p.Id)` or a monotonic
  sequence) to all four latest-selectors and include it in the covering index;
  prefer server-side ordering over client-side `GroupBy`.

### M23-5 — [Low] Missing tests for the stream-push refresh and migration edges
- **Where:** `tests/LLMCostControl.Grains.Tests/PricingGrainTests.cs` (no test
  publishes a `PricingUpdatedStreamEvent` and asserts convergence to a new version);
  `PricingStreamSubscriber` has no test; no idempotent-reapply or non-empty-table
  migration test.
- **Problem:** M23's test plan claims "PricingGrain refreshes to the new version on a
  stream push (observed across multiple activations)" (`MILESTONES.md:522-523`); the
  cited `StatelessWorker_provides_local_activations…` test only re-reads a cached
  value. Combined with M23-1 the pushed-refresh behavior is neither implemented nor
  tested; M23-2's placeholder path is untested.
- **Fix:** Add a test that seeds v1, publishes an update, and asserts the next
  `GetPricingAsync` returns v2's id/prices; add a migration test on a non-empty table.

---

## Suggested triage order

1. **X1** (snapshot/drift) and **M21-1** (untested weekly) — these block honest
   `Done/Tested/Reviewed` status.
2. **M22-1** (refresh job not wired) and **M23-2** (lossy legacy migration) — real
   functional/data-integrity gaps.
3. **M21-2, M23-1, M23-3, M22-2** — correctness/spec-contract.
4. Remaining Lows as capacity allows.

`Reviewed` stays **un-ticked** for M20–M23 until the above are addressed or
explicitly risk-accepted. Delete this file once they are closed.
