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
| M14 | Localhost pricing file import endpoint | §8.3 | M5, M7 | [x] | [x] |
| M15 | Effective-group telemetry tagging | §10.2 | M10, M11, M2 | [x] | [x] |
| M16 | Blazor admin app: scaffolding + EntraID auth | §12.1, §12.2 | M4 | [x] | [x] |
| M17 | Admin app: groups/budgets/membership/overrides CRUD | §12.3 | M16, M4 | [x] | [x] |
| M18 | Admin app: read-only views + pricing file upload | §12.3 | M17, M5 | [x] | [x] |
| M19 | Contract/conformance tests + E2E local-dev verification | §13.4, §14.3 | M13, M14, M15, M18 | [ ] | [ ] |

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

## M14 — Localhost pricing file import endpoint

- **Spec ref:** §8.3
- **Depends on:** M5, M7
- **Status:** [x] Done · **Done?** [x] · **Tested?** [x]
- **Acceptance criteria:**
  - `localhost`-bound endpoint accepts a pricing file upload and feeds it through
    the same pipeline as a live fetch (shared validator from M5, persistence via
    the refresh job's write path from M7).
  - Atomic reject on invalid file (no partial import); imported values become
    visible to `PricingGrain` via the `pricing-updated` stream.
- **Tests:**
  - Valid file → rows persisted + stream event published + grains updated;
    invalid file → 4xx, zero rows written; endpoint **not** reachable from a
    non-localhost address.

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
- **Status:** [ ] Not started · **Done?** [ ] · **Tested?** [ ]
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
