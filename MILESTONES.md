# MILESTONES — LLM Cost Tracker

> Strict ordering. **Do not start step Mn+1 until Mn is both `Done?` and
> `Tested?`.** Each milestone is "complete" only when its acceptance criteria are
> met *and* its tests pass.

Status legend: `[ ]` not started · `[~]` in progress · `[x]` done

## Summary

| # | Milestone | Spec ref | Depends on | Done? | Tested? |
| --- | --- | --- | --- | --- | --- |
| M0 | Repo & solution scaffolding | §11 | — | [x] | [x] |
| M1 | Local dev dependencies (docker compose) | §14 | M0 | [x] | [x] |
| M2 | Observability skeleton (Serilog + OTel SDK) | §10.1, §10.2* | M0, M1 | [x] | [x] |
| M3 | Domain model | §11 (Domain) | M0 | [x] | [x] |
| M4 | Infrastructure: EF Core + migrations + repositories | §11 (Infra), §9.2, §9.4 | M3, M1 | [x] | [x] |
| M5 | Pricing adapter abstraction + canonical file schema | §8.2, §8.5 | M3 | [x] | [x] |
| M6 | Provider adapters (Google, OpenAI, Anthropic) | §8.2 | M5, M4 | [x] | [x] |
| M7 | Pricing refresh job | §8.4 | M6 | [x] | [x] |
| M8 | Orleans silo host + grain interfaces + storage | §9.1, §8.6 | M4 | [x] | [x] |
| M9 | PricingGrain ([StatelessWorker] + stream sub) | §8.6 | M8, M7 | [x] | [x] |
| M10 | UserBudgetGrain: budget resolution + 30 s TTL | §7, §9.1, §12.4 | M8, M4 | [x] | [x] |
| M11 | Cost accrual + usage audit trail | §6.2.2 (cost), §9.4 | M9, M10 | [x] | [x] |
| M12 | Auth: OAuth/JWKS validation | §6.1 | M0 | [x] | [x] |
| M13 | Tracker API: check + capture endpoints | §6.2.1, §6.2.2 | M11, M12 | [x] | [x] |
| M14 | Manual pricing file upload (Admin App, §8.3) — *superseded the localhost import endpoint* | §8.3 | M5, M18 | [x] | [x] |
| M15 | Effective-group telemetry tagging | §10.2 | M10, M11, M2 | [x] | [x] |
| M16 | Blazor admin app: scaffolding + EntraID auth | §12.1, §12.2 | M4 | [x] | [x] |
| M17 | Admin app: groups/budgets/membership/overrides CRUD | §12.3 | M16, M4 | [x] | [x] |
| M18 | Admin app: read-only views + pricing file upload | §12.3 | M17, M5 | [x] | [x] |
| M19 | Contract/conformance tests + E2E local-dev verification | §13.4, §14.3 | M13, M14, M15, M18 | [x] | [x] |
| M20 | Coding standard: one top-level type per `.cs` file + guard | §17 | M19 | [x] | [x] |
| M21 | Weekly budget period (multi-period budgeting) | §6.2.1, §6.2.2, §7, §9.4, §10.2 | M20 | [x] | [x] |
| M22 | Additional providers (Azure AI Foundry, Vertex AI) | §6.2.3, §8.1, §8.2, §8.5 | M20 | [x] | [x] |
| M23 | Pricing version history (insert-on-change) + usage references version | §8.7, §9.2, §9.4 | M20, M22 | [x] | [x] |
| M24 | Running spend as a ledger-derived projection (drop grain-state persistence) | §6.2.2, §9.1, §9.3, §9.4 | M21, M23 | [ ] | [ ] |

> **Iteration 2 (M20–M24)** is owner-authorized scope added after M19. The
> coding-standard refactor (M20) lands first so all subsequent code conforms;
> M21–M23 then build on it in order. **M24** is a follow-on architectural change
> (resolved Q9) arising from the M20–M23 review: it makes the append-only ledger
> the single source of truth for running spend and supersedes review findings
> M21-2 (capture ordering / double-accrual) and M21-3 (accrual to unconfigured
> periods).

---

## M0 — Repo & solution scaffolding

- **Spec ref:** §11
- **Depends on:** —
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Git repo initialised at repo root.
  - .NET 11 solution with 4 src projects (`LLMCostControl.Domain`,
    `LLMCostControl.Infrastructure`, `LLMCostControl.Tracker.Api`,
    `LLMCostControl.Admin.App`) and 4 matching `*.Tests` projects.
  - `Directory.Build.props`, `Directory.Packages.props` (central package
    management), `.editorconfig`, `global.json` (pin SDK).
  - Baseline CI workflow (build + test on PR).
  - Both app projects boot to a "hello world" route.
- **Tests:**
  - `dotnet build` succeeds; `dotnet test` runs (empty test projects are
    discoverable and green).

## M1 — Local dev dependencies (docker compose)

- **Spec ref:** §14
- **Depends on:** M0
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `docker-compose.yml` (+ override) brings up: `postgres`, `otel-collector`,
    `loki`, `tempo`, `prometheus`, `grafana`, optional `seq`.
  - Postgres pre-created DB + user; port 5432 exposed; named volume.
  - Grafana datasources (Loki/Tempo/Prometheus) and a starter tracker dashboard
    provisioned via mounted config (no manual UI setup).
  - All services reach `healthy` on `docker compose up -d`.
- **Tests:**
  - `docker compose up -d` → all services healthy; Postgres reachable on
    `localhost:5432`; Grafana reachable on `localhost:3000` with datasources
    connected; collector accepts OTLP on `localhost:4317`.

## M2 — Observability skeleton (Serilog + OTel SDK)

- **Spec ref:** §10.1, §10.2 (partial — **without** effective-group tagging yet;
  that is M15 once grains resolve the group)
- **Depends on:** M0, M1
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Serilog wired into both apps; **console sink** in Development; sinks are
    config-driven via `appsettings.json`.
  - OpenTelemetry SDK initialised: traces, metrics, and the log bridge; OTLP
    exporter pointed at the compose collector (`localhost:4317`).
  - Resource attributes (`service.name`, `service.version`,
    `deployment.environment`) are config-driven.
  - Correlation: `requestId` (when present) and trace context attached to log
    events.
- **Tests:**
  - Boot either app in Development → logs appear in console and in Grafana/Loki;
    a trace for the hello-world route appears in Tempo; a baseline metric is
    scraped by Prometheus.

## M3 — Domain model

- **Spec ref:** §11 (`LLMCostControl.Domain`)
- **Depends on:** M0
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Pure, I/O-free entities & value objects: `CallerId`, `Money`, `BudgetPeriod`,
    `Group`, `GroupMembership`, `GroupBudget`, `UserBudgetOverride`,
    `ModelPricing`, `BudgetSource` enum (`Group` | `UserOverride` | `None`),
    `UsageEvent`.
  - No dependency on EF Core, Orleans, or any I/O library in this project.
- **Tests:**
  - `Money` arithmetic + currency-mismatch guards; `BudgetPeriod` month
    boundaries & rollover; value-object equality; `BudgetSource` semantics.

## M4 — Infrastructure: EF Core + migrations + repositories

- **Spec ref:** §11 (`LLMCostControl.Infrastructure`), §9.2, §9.4
- **Depends on:** M3, M1
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - EF Core `DbContext` + entity mappings for: groups, group memberships, group
    budgets, per-user budget overrides, model pricing (current + history), and
    the append-only `usage_events` ledger (schema per §9.4).
  - Migrations that apply cleanly against the compose Postgres.
  - Repository classes (read + write) for all of the above, shared by both apps.
  - The budget-resolution query (largest group budget for a caller, or per-user
    override) implemented in the repository layer.
- **Tests:**
  - Repository tests against **Testcontainers** Postgres: CRUD for each
    aggregate; the budget-resolution query (single group, multiple groups →
    largest wins, per-user override wins, no budget → none); migration applies
    from scratch and is idempotent.

## M5 — Pricing adapter abstraction + canonical file schema

- **Spec ref:** §8.2 (interface), §8.5 (schema)
- **Depends on:** M3
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `IPricingAdapter` interface (`FetchAsync` → `IReadOnlyCollection<ModelPricing>`).
  - Canonical pricing-file schema binding (JSON + YAML) with a **validator**.
  - Validator rejects atomically on: missing required field, `null` on mandatory
    `input`/`output`/`cacheRead`, inconsistent `currency`/`unit` across entries.
    No partial imports.
- **Tests:**
  - Validator accepts a well-formed file; rejects each malformed variant
    (missing `input`, `cacheRead` null when not allowed, mismatched currency,
    unknown provider, malformed timestamps) with a clear error and **zero** rows
    imported.

## M6 — Provider adapters (Google, OpenAI, Anthropic)

- **Spec ref:** §8.2 (hybrid strategy)
- **Depends on:** M5, M4
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - One adapter per provider, each implementing `IPricingAdapter`.
  - Each does: live fetch from the provider's published source → on failure (or
    no live source) falls back to the most recently persisted DB value → emits a
    `staleSince` staleness signal.
- **Tests:**
  - Each adapter against fixture data (sample provider payloads); simulated fetch
    failure → fallback to persisted value + staleness signal asserted; happy-path
    produces correct `ModelPricing` rows.

## M7 — Pricing refresh job

- **Spec ref:** §8.4
- **Depends on:** M6
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - A single background job (one per deployment) that iterates registered
    adapters, persists results to Postgres.
  - The **only** writer of pricing data; adapters do not write to the DB.
  - Per-provider configurable cadence (default hourly) with jitter.
  - Publishes a `pricing-updated` event onto the Orleans `pricing` stream after a
    successful persist (consumers wired in M9).
- **Tests:**
  - Job invokes each adapter and persists rows; cadence + jitter honoured; does
    not run concurrently (singularity guard); stream event published with the
    affected model names.

## M8 — Orleans silo host + grain interfaces + storage

- **Spec ref:** §9.1, §8.6
- **Depends on:** M4
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Tracker.Api hosts the Orleans silo (co-located with the API).
  - PostgreSQL storage provider configured for grain persistence.
  - Orleans stream provider configured (namespace `pricing`).
  - Grain interfaces + DTOs: `IUserBudgetGrain`, `IPricingGrain` (in a shared
    project so tests and the host can reference them).
- **Tests:**
  - Silo boots and connects to Postgres storage; `Microsoft.Orleans.TestingHost`
    TestCluster initialises; a trivial round-trip grain call succeeds; stream
    provider can publish/subscribe a test event.

## M9 — PricingGrain ([StatelessWorker] + stream subscription)

- **Spec ref:** §8.6
- **Depends on:** M8, M7
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `PricingGrain` keyed by model name; decorated `[StatelessWorker]` →
    local-only activation, multiple activations per silo.
  - Holds current `input`/`output`/`cacheRead`/`cacheWrite` prices in-memory,
    read-only on the hot path.
  - Subscribes to the `pricing-updated` stream; all activations refresh their
    in-memory value on push.
- **Tests:**
  - Stream `pricing-updated` event → every activation of the grain returns the
    new prices; placement is local (invocation does not cross silos in a
    multi-silo TestCluster); multiple activations observed under parallel load.

## M10 — UserBudgetGrain: budget resolution + 30 s TTL

- **Spec ref:** §7, §9.1, §12.4
- **Depends on:** M8, M4
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]

## M11 — Cost accrual + usage audit trail

- **Spec ref:** §6.2.2 (cost computation), §9.4 (ledger)
- **Depends on:** M9, M10
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - On capture: `UserBudgetGrain` looks up `PricingGrain` (by model), multiplies
    token counts (input/output/cacheRead/cacheWrite) by unit prices, sums to a
    total cost, accrues to running spend.
  - Exactly **one** row appended to `usage_events` per successful capture, with
    all fields per §9.4: `event_id` (idempotency key = `requestId` when given),
    `caller_id`, `effective_group_id`, `budget_source`, `model`, token counts,
    **unit-price snapshot**, `cost_amount`/`currency`, `running_spend_after`,
    `period`, `captured_at`.
  - Idempotency: duplicate capture with the same `requestId` does not
    double-accrue and does not insert a second row.
  - Unknown model → rejected with a distinct error (never zero-cost accrual).
- **Tests:**
  - Cost computation correct for mixed cached/non-cached tokens; accrual updates
    running spend; `usage_events` row has every required field incl. the
    price snapshot; duplicate `requestId` is a no-op; unknown model rejected;
    running spend reconstructible by summing rows per caller per period.

## M12 — Auth: OAuth/JWKS validation

- **Spec ref:** §6.1
- **Depends on:** M0
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Configurable JWKS / public-key endpoints (one or more issuers), expected
    `iss`/`aud`.
  - Validates signature, expiry, issuer, audience before authorising any call.
  - Token represents the **gateway**; the supplied `callerId` is trusted as
    legitimate once the gateway token validates (no end-user verification).
- **Tests:**
  - Valid token → 200; expired / bad-signature / wrong-issuer / wrong-audience /
    missing token → 401; caller id from a valid gateway call is accepted
    unchanged.

## M13 — Tracker API: check + capture endpoints

- **Spec ref:** §6.2.1, §6.2.2
- **Depends on:** M11, M12
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `POST /api/budget/check` and `POST /api/usage/capture` per the spec schemas
    (request + response + error envelopes).
  - Check returns `allowed`/`denied` + effective budget + running spend +
    remaining; capture returns computed cost + updated running spend + remaining.
  - Both require a valid gateway token (M12).
- **Tests:**
  - `WebApplicationFactory<Program>` with a stub JWKS/issuer: check
    (allow/deny/unknown-model); capture (happy path, unknown model, idempotency
    on duplicate `requestId`, `AllowNonBudgetedUsers` toggling on the check
    path).

## M14 — Manual pricing file upload (Admin App only)

> **Superseded by an owner-authorized §8.3 change.** The original M14 deliverable
> was a `localhost`-bound import endpoint. Per the repo owner's decision, that
> endpoint was **removed** and manual pricing upload is delivered exclusively
> through the Entra ID-protected Admin App (§8.3, §12.3), so there is a single,
> authenticated upload surface. `SPEC.md` §8.3 was updated accordingly. The upload
> UI and its tests live in **M18**.

- **Spec ref:** §8.3 (rewritten)
- **Depends on:** M5, M18
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Manual pricing upload is exposed **only** in the Admin App, gated by Entra ID
    and the `CostTracker.Admin` role; there is no externally reachable import
    endpoint.
  - Uploads reuse the shared canonical-file validator (M5) and the shared
    repository write path; an invalid file is rejected atomically (no partial
    import). Imported values become visible to `PricingGrain` within the 30 s
    cache TTL.
- **Tests:**
  - Covered by M18 (Admin App pricing-upload tests against real Postgres): valid
    file → rows persisted; invalid file → rejected with zero rows written.

## M15 — Effective-group telemetry tagging

- **Spec ref:** §10.2 (effective-group tagging)
- **Depends on:** M10, M11, M2
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Every span, metric data point, and log event produced while servicing a
    `check`/`capture` call carries `effective_group` and `budget_source`
    attributes, set to the same values persisted to the audit row (§9.4).
  - Values resolved by `UserBudgetGrain` at decision time and propagated into the
    current activity/log context for the request's duration.
- **Tests:**
  - For each of `budget_source = Group`, `UserOverride`, `None`: a captured
    check/capture request emits spans, metrics, and logs all carrying the
    expected `effective_group` + `budget_source` tags (asserted via the
    collector / an in-memory exporter in tests).

## M16 — Blazor admin app: scaffolding + EntraID auth

- **Spec ref:** §12.1, §12.2
- **Depends on:** M4
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `LLMCostControl.Admin.App` is a separate deployable Blazor app; does **not**
    host Orleans and does **not** call grains; reads/writes Postgres via the
    shared repositories (M4).
  - EntraID (Azure AD) OIDC sign-in; role-based auth via EntraID app roles
    (`CostTracker.Admin`, `CostTracker.ReadOnly`).
  - Does not accept the gateway token; auth surfaces are independent of the
    tracker.
- **Tests:**
  - bUnit: auth-gated rendering (admin sees commands, read-only does not,
    anonymous is redirected to sign-in); sign-in flow exercised against a
    stubbed OIDC provider.

## M17 — Admin app: groups/budgets/membership/overrides CRUD

- **Spec ref:** §12.3 (admin commands)
- **Depends on:** M16, M4
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - UI + handlers for: create/list/rename/delete group; set/update/clear group
    budget (amount + currency) for the current period; add/remove a caller id
    to/from a group; set/update/clear a per-user budget override.
  - All writes go through the shared repositories (DRY).
- **Tests:**
  - bUnit CRUD flows (create group → set budget → add member → set override →
    delete); form validation (negative/zero budget, empty caller id, duplicate
    membership); integration tests for command handlers against real Postgres.

## M18 — Admin app: read-only views + pricing file upload

- **Spec ref:** §12.3 (read-only views + pricing file management)
- **Depends on:** M17, M5
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Views: effective budget & current running spend per caller id (computed via
    shared resolution logic); current pricing per model/provider with
    `fetchedAt` + staleness; recent usage events per caller.
  - Pricing file upload through the UI → validates via the shared schema (M5) and
    writes via the shared code path (same as the tracker's localhost import,
    M14), so pricing grains pick it up.
- **Tests:**
  - Views render correctly with seeded data (a caller in two groups shows the
    largest budget as effective; staleness badge renders on stale pricing);
    upload of a valid file updates pricing; upload of an invalid file shows the
    validator's errors and writes nothing.

## M19 — Contract/conformance tests + E2E local-dev verification

- **Spec ref:** §13.4, §14.3
- **Depends on:** M13, M14, M15, M18
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - Contract tests fix the gateway-facing API shape (request/response schemas
    for check/capture, error envelopes) so breaking changes fail CI.
  - A repo-provided `requests.http` / curl script for manual probing.
  - The full §14.3 workflow verified: `docker compose up -d` → F5 Tracker → F5
    Admin → issue sample check/capture → see traces/logs in Grafana → see spend
    accrue in the admin app.
- **Tests:**
  - Contract suite green in CI; E2E checklist (manual, documented in the repo)
  signed off.

---

# Iteration 2 — owner-authorized scope additions

## M20 — Coding standard: one top-level type per `.cs` file + guard

- **Spec ref:** §17
- **Depends on:** M19
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Rationale:** lands first so every subsequent milestone's code is authored
  one-type-per-file and is checked by the guard from the outset.
- **Acceptance criteria:**
  - Every production `.cs` file under `src/` that currently declares more than one
    top-level type (e.g. an interface co-located with its implementation, or two
    enums) is **split** so each file declares exactly **one** top-level type; the
    file name matches the type it declares (§17).
  - An automated **architecture test** parses each production source file's syntax
    tree (Roslyn / `Microsoft.CodeAnalysis.CSharp`) and asserts at most one
    top-level type per file, honouring the §17 exemptions (`Program.cs` top-level
    statements; generated EF `*.Designer.cs` / model snapshot; `obj/` `bin/`).
    Nested types and `partial` types across files do not count as violations.
  - The solution builds with **0 warnings / 0 errors** (`TreatWarningsAsErrors`),
    and **all pre-existing tests stay green** — this is a pure refactor with no
    behaviour change.
- **Tests:**
  - The guard test passes against the refactored tree, and is proven to actually
    guard: a fixture string declaring two top-level types is detected as a
    violation (so an accidental regression would fail CI). All existing suites
    remain green.

## M21 — Weekly budget period (multi-period budgeting)

- **Spec ref:** §6.2.1, §6.2.2, §7, §9.4, §10.2
- **Depends on:** M20 (and builds on M3, M10, M11, M13, M15, M17)
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `BudgetPeriodType` enum (`Monthly`, `Weekly`); `BudgetPeriod` generalised to
    identify a concrete instance of either type — calendar month, or **ISO-8601
    week starting Monday (UTC)** — exposing `[Start, End)`, `Contains`,
    `Next`/`Previous`, and the `YYYY-MM` / `YYYY-Www` keys (§7).
  - `GroupBudget` and `UserBudgetOverride` carry a `PeriodType`; uniqueness is
    `(GroupId, PeriodType)` and `(CallerId, PeriodType)` respectively. Shared
    repositories + the admin write path updated; EF migration applies cleanly.
  - **Per-period effective-budget resolution** producing an effective-budget
    **set**: for each period type, the per-user override of that type wins, else
    the **largest group budget of that type**; a period type with no configured
    budget is excluded from the set (§7).
  - `UserBudgetGrain` tracks running spend **per period type**, accrues each
    capture's cost to **every** configured dimension, rolls each dimension over
    independently, and decides allow/deny as the **AND** across configured periods
    (any zero/exhausted period → deny; empty set → governed by
    `AllowNonBudgetedUsers`).
  - `/api/budget/check` and `/api/usage/capture` return the per-period `budgets`
    array (§6.2.1, §6.2.2); error envelopes unchanged.
  - Audit (§9.4): exactly one `usage_events` row per capture **plus** one
    `usage_event_period_accruals` child row per accrued dimension
    (`period_type`, `period_key`, `effective_group_id`, `budget_source`,
    `effective_budget_amount`, `running_spend_after`); idempotency on `requestId`
    preserved; migration applies cleanly.
  - Telemetry (§10.2): spans/metrics/logs carry a `budget_period` tag and the
    **binding-period** `effective_group` / `budget_source`.
  - Admin App (§12.3): set / update / clear a group's **Monthly and Weekly**
    budgets, and **Monthly / Weekly** per-user overrides.
- **Tests:**
  - **Domain:** weekly boundary + rollover (ISO week, Monday start), period-key
    formatting, per-period resolution incl. largest-within-type and
    override-of-type-wins.
  - **Grain:** one capture accrues to both monthly + weekly; dimensions roll over
    independently; allowed only when *every* configured period has remaining;
    zero-budget period cut-off; unbudgeted via `AllowNonBudgetedUsers`.
  - **API / contract:** per-period response shape for check & capture (allow when
    all periods pass, deny when any fails); contract suite updated.
  - **Audit:** child rows written per dimension; zero child rows for an
    unbudgeted-allowed capture; weekly and monthly running spend reconstructible
    from the ledger.
  - **Admin (bUnit + real Postgres):** set weekly + monthly on a group; per-period
    override; validation (negative rejected, zero accepted).
  - **Telemetry:** `budget_period` + binding-period tags asserted via an in-memory
    exporter.

## M22 — Additional providers (Azure AI Foundry, Vertex AI)

- **Spec ref:** §6.2.3, §8.1, §8.2, §8.5
- **Depends on:** M20 (and builds on M5, M6, M9)
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `Provider` enum gains `AzureFoundry` and `VertexAI`; canonical-file / string
    mapping `azure-foundry` and `vertex-ai` (§8.5); provider parse/serialise paths
    (validator, DTOs, repositories, `"{provider}:{model}"` grain key) updated.
  - Two adapters — `AzureFoundryPricingAdapter`, `VertexAIPricingAdapter` —
    implementing `IPricingAdapter` via `PricingAdapterBase` (live fetch →
    persisted fallback → staleness), registered with the refresh job.
  - Provider resolution (§6.2.3): an explicit `azure-foundry` / `vertex-ai` is
    accepted; the inference prefix-map carries **no** entries for them (a
    provider-omitted `gpt*` / `gemini*` / `claude*` still resolves to its native
    vendor); capture under an explicit hosting provider prices via that exact
    `(provider, model)`.
  - `PricingFileValidator` accepts files declaring the two new providers.
- **Tests:**
  - Each new adapter against fixture payloads: happy path → correct
    `ModelPricing`; simulated fetch failure → fallback to persisted + staleness
    signal asserted.
  - Provider resolution: explicit foundry/vertex resolved; provider omitted +
    `gpt*` → `openai` (not foundry); unknown provider → unknown-model reject.
  - Validator accepts the new provider strings; capture for
    `(vertex-ai, claude-3-5-sonnet)` prices independently of
    `(anthropic, claude-3-5-sonnet)`.

## M23 — Pricing version history (insert-on-change) + usage references version

- **Spec ref:** §8.7, §9.2, §9.4
- **Depends on:** M20, M22 (and builds on M4, M7, M9, M11)
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `model_pricing` becomes **append-only versioned**: immutable rows with a
    surrogate id + `EffectiveFrom`; **no upsert**. The repository exposes
    "latest version for `(provider, model)`" and an "insert-version-if-changed"
    write.
  - **Insert-on-change** in the §8.4 refresh job and the §8.3 Admin upload (the
    only two writers): compare incoming prices to the latest version and insert a
    new version **only** when `input`/`output`/`cacheRead`/`cacheWrite`/`currency`/
    `unit` differ; first sighting inserts unconditionally; otherwise a no-op.
  - Current price = latest by `EffectiveFrom` (covering index). Staleness is
    **derived** from the latest version's `fetchedAt` + cadence — version rows are
    never mutated (§8.7).
  - The `pricing-updated` stream event carries the new **version id(s)**;
    `PricingGrain` caches the current version's id + prices and refreshes on push.
  - `UsageEvent` / `usage_events` replaces the embedded `unit_prices`
    (`TokenPrices`) with a `pricing_version_id` FK (§9.4); capture records the
    version it priced with; FK retention prevents pruning a referenced version;
    migration applies cleanly.
- **Tests:**
  - Insert-on-change: first insert creates v1; an identical re-fetch adds **no**
    row; a changed price creates v2; `current()` returns v2.
  - Capture writes `pricing_version_id` referencing the current version;
    idempotency on `requestId` unaffected; cost equals tokens × the referenced
    version's prices (reconstructable from the ledger).
  - `PricingGrain` refreshes to the new version on a stream push (observed across
    multiple activations).
  - Admin upload inserts a version on change and is a no-op when unchanged.
  - Migration applies from scratch and is idempotent (Testcontainers Postgres).

## M24 — Running spend as a ledger-derived projection (drop grain-state persistence)

- **Spec ref:** §6.2.2, §9.1, §9.3, §9.4 (resolved Q9)
- **Depends on:** M21 (per-period accrual child rows), M23 (pricing version); builds on M11
- **Status:** [ ] Not started · **Done?** [ ] · **Tested?** [ ]
- **Rationale:** the M20–M23 review found that capture persists running spend to
  Orleans grain state **before** appending the audit row and ignores the append
  result, leaving a double-accrual window on crash/reactivation (REVIEW.md M21-2),
  and that spend accrues to unconfigured period dimensions (REVIEW.md M21-3). Per
  the owner's decision (option A, resolved Q9) the fix is architectural: make the
  append-only ledger the single source of truth and derive running spend from it,
  rather than reorder a dual write.
- **Acceptance criteria:**
  - `UserBudgetGrain` no longer persists running spend as Orleans grain state:
    the running-spend fields are removed from the persisted grain state (and, if
    no persisted state remains, the `[PersistentState]`/Orleans storage dependency
    for this grain is removed). Any now-unused Orleans grain-storage provider
    registration is removed or explicitly documented as retained.
  - On **activation**, the grain **reconstructs** the current running spend for
    **each configured period type** by summing the append-only `usage_events`
    ledger (§9.4) over the current period instance, and keeps it warm in memory for
    the activation's lifetime (§9.1).
  - Running spend is derived **only** from the per-configured-dimension accrual
    rows / ledger, so an unconfigured period type has no running spend — this
    **supersedes M21-3** (no accrual to unconfigured dimensions).
  - **Capture is append-first (§9.3):** the grain appends the `usage_events` row
    (+ per-period accrual child rows) — idempotent on the `event_id` primary key —
    **before** updating its in-memory projection. A duplicate `requestId` neither
    inserts a second row nor increments spend; a crash/reactivation between append
    and increment is self-healing (spend recomputed from the ledger). This
    **supersedes M21-2** (double-accrual window).
  - Behaviour visible to the API is unchanged: check/capture still return the
    per-period `budgets` array with correct running spend / remaining (§6.2.1,
    §6.2.2), and the per-period AND-deny / zero-cut-off / `AllowNonBudgetedUsers`
    semantics (§7) are preserved.
- **Tests:**
  - On a fresh activation with pre-seeded ledger rows, running spend for each
    period type equals the ledger sum for the current period instance.
  - Capture appends **before** incrementing; simulating a fresh activation (lost
    in-memory state) after a capture yields the correct spend rebuilt from the
    ledger — **no double count**.
  - Duplicate `requestId`: no second ledger row and no double increment, including
    a **cross-activation** variant (deactivate between the two calls).
  - Period rollover: the projection resets for the rolled-over dimension by
    recomputing against the new period instance, independently per type.
  - Multi-period: running spend for both weekly and monthly is rebuilt correctly
    from the ledger; an unconfigured period type contributes nothing.
  - Regression: the existing M21 check/capture/idempotency suites stay green.
