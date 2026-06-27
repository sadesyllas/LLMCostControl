# SPEC — LLM Cost Tracker

> Status: Draft (iteration 1 — per-person budgets)
> Target framework: .NET 11

## 1. Overview

A self-contained service that tracks LLM spend per caller and enforces budgets.
It is designed to sit **behind** an LLM gateway: the gateway calls the tracker
twice per LLM request — once before forwarding the request to the model provider
(to check remaining budget), and once before returning the response to the caller
(to capture token usage and accrue cost).

The tracker is **gateway-agnostic**: it makes no assumptions about which LLM
gateway integrates with it, and it makes no assumptions about which LLM provider
served a given request beyond the model name reported by the gateway.

## 2. Goals (iteration 1)

1. Authenticate gateway calls via an OAuth bearer token validated against
   configurable JWKS endpoints.
2. Provide a pre-request **budget check** for a caller id (typically an email).
3. Provide a post-response **usage capture** that records token usage
   (input / output / cache-read, optionally cache-write) for a named model and
   accrues cost against the caller.
4. Maintain warm, up-to-date **pricing** for allowed models across Google,
   OpenAI and Anthropic, fetched per-provider via pluggable adapters.
5. Resolve the **effective budget** for a caller from group membership, with an
   optional explicit per-user override.
6. Keep per-caller running state warm in **Microsoft Orleans**, backed by
   **PostgreSQL** for persistence.

## 3. Non-goals (iteration 1)

- Group membership administration UI / API (deferred — see §10).
- Setting group budgets via API (deferred — see §10).
- Budgets at scopes other than per-person (e.g. team/org) — future iteration.
- Multi-tenancy of the tracker itself.
- Streaming/partial capture semantics (capture is a single post-response call).

## 4. Glossary

| Term | Meaning |
| --- | --- |
| **Gateway** | The external LLM gateway that calls this tracker. Its identity is unknown to us. |
| **Caller id** | The identity of the end user/consumer of the LLM call, typically an email. This is the budget subject. |
| **Model** | A provider model name as reported by the gateway (e.g. `gpt-4o`, `claude-3-5-sonnet`, `gemini-1.5-pro`). |
| **Adapter** | A per-provider component that knows how to fetch current pricing for that provider's models. |
| **Effective budget** | The budget that applies to a caller id after resolving groups + user override. |
| **Running spend** | The accumulated cost for a caller id within the current budget period. |

## 5. High-level architecture

```
            ┌───────────────────────────┐
            │      LLM Gateway          │
            │  (unknown, pluggable)     │
            └─────────────┬─────────────┘
                  (1) check        (2) capture
                    │                 │
                    ▼                 ▼
        ┌─────────────────────────────────────┐
        │        LLM Cost Tracker (.NET 11)   │
        │  ┌───────────────────────────────┐  │
        │  │  Auth: OAuth/JWKS validation  │  │
        │  ├───────────────────────────────┤  │
        │  │  HTTP API (check / capture)   │  │
        │  ├───────────────────────────────┤  │
        │  │  Microsoft Orleans (silos)    │  │
        │  │   • UserBudgetGrain / caller  │  │
        │  │   • PricingGrain (per model)  │  │
        │  ├───────────────────────────────┤  │
        │  │  Pricing refresh background   │  │
        │  │  job → provider adapters      │  │
        │  └───────────────────────────────┘  │
        └────────────────┬────────────────────┘
                         │
                         ▼
                ┌──────────────────┐
                │   PostgreSQL     │
                │  (persistence)   │
                └──────────────────┘
```

## 6. Integration contract

### 6.1 Authentication

- The gateway presents an **OAuth bearer token** in a header on every call to
  the tracker.
- The tracker is **configured** (not hard-coded) with:
  - The JWKS / public-key endpoints to fetch signing keys from (one or more
    issuers).
  - The expected issuer (`iss`) and/or audience (`aud`) claims, if any.
- The tracker validates the token's signature, expiry, issuer and audience
  before authorising any call.
- **Token subject (resolved Q1):** The token represents the **gateway** as a
  service principal, not the end user. The caller id is supplied by the gateway
  as a parameter on each call. If the gateway's token is valid (authentic,
  unexpired, correct issuer/audience), the tracker accepts the supplied caller id
  as legitimate without performing any additional verification of the end user's
  identity. (End-user token binding may be revisited in a future iteration.)
- **Open question (Q1 — superseded):** n/a.

### 6.2 Endpoints

Two endpoints are exposed to the gateway. Request/response bodies are
indicative; exact schema to be finalised.

#### 6.2.1 Budget check — pre-request

Called by the gateway before forwarding the request to the model provider.

```
POST /api/budget/check
Authorization: Bearer <gateway token>

{
  "callerId": "alice@example.com",     // budget subject (typically email)
  "model": "gpt-4o"                    // optional, for early model gating
}
```

Response:

```
{
  "allowed": true,
  "callerId": "alice@example.com",
  "effectiveBudget": { "amount": 50.0, "currency": "USD" },
  "runningSpend": { "amount": 12.34, "currency": "USD" },
  "remaining": { "amount": 37.66, "currency": "USD" }
}
```

- Returns `allowed: false` when remaining budget ≤ 0 (or below a configurable
  threshold).
- The tracker does **not** reserve/hold any amount at check time in iteration 1
  (no pending holds). Concurrency note: a caller may overspend between check and
  capture if many requests are in flight; accepted for iteration 1.

#### 6.2.2 Usage capture — post-response

Called by the gateway after receiving the provider response, before returning it
to the caller.

```
POST /api/usage/capture
Authorization: Bearer <gateway token>

{
  "callerId": "alice@example.com",
  "model": "gpt-4o",
  "tokens": {
    "input": 1234,            // non-cached input tokens
    "output": 567,            // generated tokens
    "cacheRead": 890,         // cached input tokens (provider cache hit)
    "cacheWrite": 0           // optional; tokens written to cache
  },
  "requestId": "uuid-...",    // optional idempotency key
}
```

Response:

```
{
  "callerId": "alice@example.com",
  "cost": { "amount": 0.0123, "currency": "USD" },
  "runningSpend": { "amount": 12.3523, "currency": "USD" },
  "remaining": { "amount": 37.6477, "currency": "USD" }
}
```

- The tracker resolves the model's current **input / output / cache-read /
  cache-write** unit prices, multiplies by the reported token counts, sums to a
  total cost, and accrues it against the caller's running spend.
- `requestId`, when present, is used for **idempotency**: a duplicate capture
  with the same `requestId` must not double-accrue.
- If the model is unknown / not in the allowed pricing set, capture is rejected
  with a distinct error (the gateway should not be silently accruing at zero
  cost).

## 7. Budget model

- A **budget** is an amount of money (with currency) that applies to a budget
  subject for the current budget period.
- Budget period for iteration 1: **calendar month**, resetting on the first day
  of the month. (Open: configurable period — deferred.)
- Budgets are attached to **groups**. A caller id may belong to zero or more
  groups.
- **Effective budget resolution** for a caller id, in priority order:
  1. If an **explicit per-user budget** is set on the caller id, that value
     applies (special-case override).
  2. Otherwise, among all groups the caller belongs to, the **largest** budget
     applies.
   3. If the caller belongs to no group and has no explicit budget, the caller
      has **no budget**. The check is **fail-closed by default**: such a caller
      is denied. This is configurable via the application setting
      `AllowNonBudgetedUsers` (default `false`); when set to `true`, unbudgeted
      callers are allowed through the check (their spend is still recorded for
      audit, but the check never gates them).
- Running spend is tracked per caller id and resets with the period.
- Membership and budget administration (how a person is added to a group, how a
  group's budget is set, how a per-user override is set) is **out of scope** for
  iteration 1 and will be specified in a follow-up (see §10).

## 8. Pricing model & provider adapters

### 8.1 Pricing shape

For each allowed model, the tracker keeps these unit prices (per 1M tokens,
consistent unit across providers; currency normalized where feasible):

- `input` — non-cached input tokens
- `output` — generated tokens
- `cacheRead` — cached input tokens (provider cache hit; typically cheaper than
  `input`)
- `cacheWrite` — tokens written to the provider cache (where applicable; may be
  unset for models/providers without a cache-write concept)

Each model entry also records: provider, model name (as the gateway reports it),
the timestamp the pricing was fetched, and a version/etag if the source provides
one.

### 8.2 Adapters

- One adapter per provider: **Google**, **OpenAI**, **Anthropic**.
- An adapter implements a common interface:
  - `Task<IReadOnlyCollection<ModelPricing>> FetchAsync(CancellationToken ct)`
- Each adapter encapsulates the specific way that provider's pricing is obtained
  (official pricing API where one exists, otherwise a documented,
  provider-specific mechanism). The concrete source per provider is an
  implementation detail of the adapter; the rest of the system is unaware.
- **Pricing source strategy (resolved Q3 — hybrid):**
  1. Each adapter fetches live pricing from the provider's published source
     when one is available.
  2. On fetch failure (or no live source for that provider), the adapter falls
     back to the most recently persisted value in the DB and emits a
     **staleness signal** (e.g. a `staleSince` timestamp and the age of the
     last good value) so consumers can react or alert.
  3. In addition, the system supports a **manual pricing file upload** interface
     served entirely within the Entra ID-protected Admin App (see §8.3). The file must
     fully describe all required values per model, per provider, in a single,
     well-defined schema (see §8.5). This enables offline/air-gapped operation
     and manual correction of pricing without depending on a live provider
     source.

### 8.3 Manual pricing file upload (Admin App only)

- A dedicated file upload interface is exposed in the Admin App, protected by
  Entra ID and requiring the `CostTracker.Admin` role.
- The file format must comply with the canonical schema defined in §8.5. The upload
  is validated via a shared parser and is rejected atomically if any model entry is
  missing a required field or is malformed (no partial imports).
- Imported values are written directly to PostgreSQL using the shared repository
  code, and become visible to pricing grains automatically within their 30-second cache
  TTL (see §8.6).

### 8.4 Pricing refresh job

- A **single** background job (one per tracker deployment) is responsible for
  keeping pricing warm.
- The job iterates the registered adapters (one per provider), invokes
  `FetchAsync`, and **persists** the results into PostgreSQL.
- Refresh cadence is configurable per provider (default e.g. hourly), with
  jitter.
- The job is the **only** writer of pricing data. Adapters do not write to the
  DB directly; the job does.

### 8.5 Canonical pricing file schema

The file (used by the Admin App manual upload and as the reference shape
for adapter fetch results) must describe, **per provider, per model**, all
required unit prices. The schema (shown here as JSON for clarity; the on-disk
format may be JSON or YAML):

```json
{
  "generatedAt": "2026-06-24T12:00:00Z",
  "currency": "USD",
  "unit": "per-1M-tokens",
  "providers": [
    {
      "provider": "openai",
      "models": [
        {
          "model": "gpt-4o",
          "fetchedAt": "2026-06-24T12:00:00Z",
          "prices": {
            "input":     2.50,
            "output":    10.00,
            "cacheRead":  1.25,
            "cacheWrite": null
          }
        }
      ]
    },
    {
      "provider": "anthropic",
      "models": [ /* ... */ ]
    },
    {
      "provider": "google",
      "models": [ /* ... */ ]
    }
  ]
}
```

Required fields per model entry:

- `provider` — one of `openai` | `anthropic` | `google` (extensible).
- `model` — the model name **as the gateway reports it** (this is the join key
  used by the capture path).
- `fetchedAt` — ISO-8601 timestamp of when these prices were obtained.
- `prices.input`, `prices.output` — mandatory, non-null.
- `prices.cacheRead` — mandatory; `null` only if the model genuinely has no
  cache concept.
- `prices.cacheWrite` — optional; `null` when not applicable.
- `currency`, `unit` — declared at the file level; all entries must conform.

A file missing any required field, or declaring inconsistent `currency`/`unit`
across entries, is rejected at import time.

### 8.6 Pricing grains (distinct from per-user cost grains)

There are two, separate kinds of grains involved in cost tracking. They must not
be conflated:

1. **`UserBudgetGrain`** — keyed by caller id. Tracks that caller's running spend
   for the current period, resolves the effective budget, and answers
   check/capture calls. Described in §9.1.
2. **`PricingGrain`** — keyed by model name. Holds that model's current unit
   prices (`input`, `output`, `cacheRead`, `cacheWrite`) in-silo so the capture
   path can compute cost without a DB round-trip on every call.

When a capture arrives, the `UserBudgetGrain` for the caller looks up the
relevant `PricingGrain` (by model name), multiplies the reported token counts by
the grain's current unit prices, sums to a total cost, and accrues it. The
`PricingGrain` is read-only on the hot path.

**Update mechanism (resolved Q4 — Orleans streams, push):** When the background
refresh job (§8.4) writes new prices to PostgreSQL, it publishes a
`pricing-updated` event (carrying the affected model names, or a full snapshot)
onto an **Orleans stream** (`StreamNamespace = "pricing"`). `PricingGrain`
instances subscribe to this stream and refresh their in-memory value on push,
giving near-immediate freshness after a live fetch, or within the 30-second cache TTL after an Admin App manual upload,
with no polling tax on the DB.

**Grain placement — `PricingGrain` is a `[StatelessWorker]` local grain:**

- `PricingGrain` is decorated with Orleans' `[StatelessWorker]` placement
  strategy. This has two important consequences:
  1. **Local-only activation:** a `PricingGrain` is always activated **on the
     same silo** as the caller (the `UserBudgetGrain` servicing a capture). It is
     never invoked across silos — the runtime hands back a local activation, so
     pricing lookups on the hot path are an in-process call, not a network hop.
  2. **Multiple instances per silo:** each silo may host several activations of
     the same `PricingGrain` (Orleans creates them on demand, up to a configured
     max, to parallelise load). This removes any single-activation bottleneck on
     the read path while still keeping the data set small enough that every
     activation holds the full, current pricing for its model.
- All activations of a given `PricingGrain` subscribe to the `pricing-updated`
  stream, so every local copy converges to the latest persisted value.
- Because pricing is read-mostly and small, the per-silo memory cost of multiple
  activations is negligible; the benefit is contention-free, local reads on the
  capture path.

## 9. State management & persistence

### 9.1 Orleans

- Per-caller running state is held in a grain keyed by caller id
  (`UserBudgetGrain`), keeping the current period's running spend warm in memory.
- The grain is responsible for: reading effective budget (possibly via another
  grain / storage), accruing capture costs, computing remaining budget, and
  answering checks.
- Grain state is persisted to PostgreSQL via Orleans storage providers so that
  silo restarts do not lose accrued spend.

### 9.2 PostgreSQL

PostgreSQL is the single source of truth for:
- Per-caller running spend (per period) — Orleans grain storage.
- Pricing history / current pricing per model — written by the refresh job.
- Groups, group membership, group budgets, per-user budget overrides — written
  by the (deferred) administration surface; read by the budget resolution path.
- Captured usage events (append-only ledger) — for audit and period rebuild.

### 9.3 Concurrency & consistency

- Capture accrual on a grain is serialised per caller id (single-threaded grain
  activation), which is sufficient for iteration 1.
- The usage ledger is append-only; running spend is the authoritative live value
  in the grain, periodically/transactionally flushed to the DB.

### 9.4 Usage audit trail (per-capture ledger rows)

- For **every** successful `capture` call (i.e. every LLM response for which the
  tracker updates a caller's running cost), exactly **one new row** is appended
  to the `usage_events` table in PostgreSQL.
- A row contains, at minimum:
  - `event_id` — unique id (the `requestId` from the capture, when supplied, is
    used as the idempotency/natural key; otherwise a generated GUID).
  - `caller_id` — the end-user id (typically email).
  - `effective_group_id` — the **group whose budget was in effect** for this
    capture (the group that provided the largest active budget, or `null` if the
    caller is unbudgeted / per-user-override). Captured at capture time so the
    audit row reflects the budget source used for the decision, even if membership
    changes later.
  - `budget_source` — enum: `Group` | `UserOverride` | `None`.
  - `model` — the model name reported by the gateway.
  - `tokens_input`, `tokens_output`, `tokens_cache_read`, `tokens_cache_write`.
  - `unit_prices` — snapshot of the input/output/cacheRead/cacheWrite unit prices
    used to compute the cost (so a future pricing change never rewrites history).
  - `cost_amount`, `cost_currency` — the computed cost.
  - `running_spend_after` — the caller's running spend after this capture.
  - `period` — the budget period (e.g. `2026-06` for monthly).
  - `captured_at` — server-side timestamp.
- The ledger is **append-only**: no updates, no deletes. Running spend can be
  reconstructed by summing `cost_amount` per `caller_id` per `period`.
- Snapshotting/historicity (compaction, archival, point-in-time queries over
  running spend) is explicitly deferred — see §15.

## 10. Non-functional requirements

### 10.1 Logging

- **Serilog** is the logging framework. All application logging flows through the
  Serilog pipeline.
- **Development environment** uses the **console sink** (pretty-formatted) as the
  primary sink.
- Sinks are configuration-driven via `appsettings.json` / environment-specific
  overrides so production sinks (e.g. OTLP, Seq, file) can be added without code
  changes. Structured logging is required throughout (no message-template
  interpolation of secrets).
- Correlation ids (e.g. the `requestId` from capture calls, and the trace
  context — see §10.2) are attached to every log event.

### 10.2 Observability — OpenTelemetry

- The tracker is **fully instrumented** with the **OpenTelemetry SDK**:
  - **Traces** (distributed tracing) for all inbound HTTP requests (check /
    capture) and outbound calls (provider adapter fetches,
    DB access via Orleans storage).
  - **Metrics** exposing at minimum:
    - Request counters & histograms per endpoint (check, capture), with outcome
      labels (`allowed`/`denied`, `accepted`/`rejected`).
    - Capture cost metrics: cost per capture, tokens captured (input/output/
      cacheRead/cacheWrite) per model.
    - Pricing freshness/staleness gauge per provider, and a counter of fetch
      failures / fallbacks.
    - Grain-level metrics where Orleans exposes them (activations, etc.).
  - **Logs** are bridged into the OTLP pipeline via the OpenTelemetry Serilog
    sink / bridge so logs, metrics and traces share a single correlation context.
- Export is via **OTLP** by default (endpoint configurable), enabling pluggable
  backends (e.g. local OTel collector, Jaeger, Tempo, Prometheus, Grafana).
- The OTLP exporter endpoint and resource attributes (service.name, service.
  version, deployment.environment) are configuration-driven.
- **Effective-group tagging (required):** every span, metric data point, and log
  event produced while servicing a `check` or `capture` call **must** carry the
  `effective_group` (the id/name of the group whose budget was in effect for that
  decision) and `budget_source` (`Group` | `UserOverride` | `None`) as attributes
  / labels / tags. This ties all telemetry about a request to the specific
  group-level budget against which the allow/deny and cost accrual were computed,
  so dashboards and alerts can be sliced by group. The values are resolved by the
  `UserBudgetGrain` at decision time (the same values persisted to the audit row
  per §9.4) and propagated into the current activity/log context for the duration
  of the request.

### 10.3 Configuration

- All environment-specific values (JWKS endpoints, issuer/audience, DB
  connection string, `AllowNonBudgetedUsers`, pricing refresh cadence, OTLP
  endpoint, Serilog sinks) live in `appsettings.json` with environment-specific
  overrides and environment-variable binding for secrets.
- No secrets are hard-coded or committed.

## 11. Solution structure (monorepo, DRY)

The tracker and the admin app are separate deployables that share as much code
as possible. They live in a single git repository (monorepo) and share common
libraries. Indicative layout:

```
/ (repo root)
├── docker-compose.yml              # local dev dependencies (see §14)
├── docker-compose.override.yml     # dev-only tweaks
├── SPEC.md
├── src/
│   ├── LLMCostControl.Domain/         # shared domain model, pure entities,
│   │                                  #   value objects, enums (no I/O)
│   ├── LLMCostControl.Infrastructure/ # EF Core DbContext + migrations,
│   │                                  #   repository implementations,
│   │                                  #   Postgres-specific concerns,
│   │                                  #   pricing adapter abstractions &
│   │                                  #   implementations
│   ├── LLMCostControl.Tracker.Api/    # ASP.NET Core API host (check/capture endpoints) +
│   │                                  #   Orleans silo host (UserBudgetGrain,
│   │                                  #   PricingGrain, refresh job)
│   └── LLMCostControl.Admin.App/      # Blazor admin app (see §12)
└── tests/
    ├── LLMCostControl.Domain.Tests/
    ├── LLMCostControl.Infrastructure.Tests/
    ├── LLMCostControl.Tracker.Api.Tests/
    └── LLMCostControl.Admin.App.Tests/
```

**Shared (lives in `Domain` / `Infrastructure` and is referenced by both apps):**

- Domain entities & value objects: `Group`, `GroupMembership`, `GroupBudget`,
  `UserBudgetOverride`, `ModelPricing`, `CallerId`, `Money`, `BudgetPeriod`, etc.
- EF Core `DbContext`, entity mappings, and **migrations** (a single source of
  truth for the schema, applied by whichever app boots first / by a dedicated
  migrate step).
- Repository classes (read + write) for groups, memberships, budgets, pricing,
  and the usage ledger. Both the tracker (read-side on the budget resolution
  path, write-side for usage) and the admin app (read + write of admin entities)
  use these.
- The pricing **adapter abstraction** (`IPricingAdapter`) and the canonical
  pricing file schema binding (§8.5) — shared so the admin app can render and
  validate pricing files too.
- The Orleans grain interfaces (`IUserBudgetGrain`, `IPricingGrain`) and their
  DTOs — referenced by the tracker host (implementations) and by any in-process
  test silo; the admin app talks to the **DB** for admin entities, not to grains,
  but may read pricing via the repository.

**App-specific (not shared):**

- The tracker's HTTP controllers, Orleans silo configuration, refresh job host,
  auth middleware (gateway token / JWKS validation).
- The admin app's Blazor UI, EntraID auth wiring, admin-only command handlers.

## 12. Admin center — Blazor app

The administration surface is a **Blazor web application**, a **separate
deployable** from the tracker API. It is the human-facing admin center for
budgets, groups, and pricing visibility.

### 12.1 Topology

Two distinct flows, two distinct deployables:

```
Gateway ──►  Cost Tracker API  ──►  Grains (UserBudgetGrain / PricingGrain)
              (gateway token auth)        │
                                            ▼
                                         PostgreSQL

Admin user ──►  Blazor Admin App  ──►  PostgreSQL
                 (EntraID auth)          (via shared repositories)
```

- The Blazor admin app does **not** host Orleans and does **not** call the
  tracker's grains directly. It reads/writes the shared PostgreSQL schema via
  the shared repository classes from `LLMCostControl.Infrastructure`.
- Rationale: admin operations (create group, set budget, add member) are
  low-frequency and operate on tables that grains only **read** (for budget
  resolution) or don't touch at all. A grain call would add latency and coupling
  without benefit. Grains pick up budget/group changes via their normal read path
  (with a short cache TTL or an explicit refresh, see §12.4).

### 12.2 Authentication & authorization

- **EntraID (Azure AD)** is the identity provider for the admin app.
- The app is registered as an EntraID application; sign-in uses the Microsoft
  identity platform (OpenID Connect).
- Authorization is **role-based**, driven by EntraID app roles (e.g.
  `CostTracker.Admin`, `CostTracker.ReadOnly`). Group/role assignment is managed
  in EntraID, not in the tracker's DB.
- The admin app does **not** accept the gateway's OAuth token and does **not**
  expose the tracker's check/capture endpoints. The two auth surfaces are
  completely independent.

### 12.3 Admin capabilities (iteration 1 scope)

- **Groups:** create / list / rename / delete a group.
- **Group budgets:** set / update / clear the budget amount (and currency) for a
  group, for the current budget period.
- **Group membership:** add / remove a caller id (email) to/from a group.
- **Per-user budget overrides:** set / update / clear an explicit budget for a
  specific caller id (the special-case override that wins over group budgets,
  see §7).
- **Read-only views:**
  - Effective budget & current running spend per caller id (resolved through the
    same rules the tracker uses, computed in-app from the shared repos).
  - Current pricing per model/provider, with `fetchedAt` and staleness status.
  - Recent usage events (from the append-only ledger) for a caller id.
- **Pricing file management:** upload a canonical pricing file (§8.5) through the
  UI. The admin app validates it against the shared schema and **writes it to
  PostgreSQL via the same code path the tracker uses** (shared from `Infrastructure`),
  so the pricing grains pick it up automatically within their 30-second cache TTL
  (see §8.3 and §8.6).

### 12.4 Consistency with grains

- Grains cache effective budgets per caller id for the check path. To keep the
  admin app's writes visible to the tracker without a restart, budget/group data
  read by `UserBudgetGrain` is treated as **short-TTL cached**.
- **Resolved Q5 — 30 s TTL:** each `UserBudgetGrain` reloads the user's active
  budget (effective group / per-user override) from the DB **at most every 30
  seconds**. Concretely: the grain holds a `(value, loadedAt)` pair; on read it
  returns the cached value if `now - loadedAt < 30 s`, otherwise it re-reads
  from the DB (via the shared repository), updates the cache, and then answers.
  The TTL is configurable via `BudgetCacheTtlSeconds` (default `30`).
- This means an admin-side change to a group budget or membership becomes visible
  to the check path within at most ~30 s — acceptable for iteration 1. (A
  stream-based push invalidation is deferred — see §15.)

## 13. Testing

Thorough automated testing is a first-class requirement, not an afterthought.

### 13.1 Framework & conventions

- **xUnit** is the test framework; **FluentAssertions** for assertions; **Moq**
  (or NSubstitute) for mocks where needed.
- Test projects mirror the solution structure (§11). Each production project has
  a matching `*.Tests` project.
- Tests run in CI and are expected to be green for merge.

### 13.2 Unit tests

- **Domain:** budget resolution rules (group-largest, per-user override wins,
  no-budget → deny/allow per config), cost computation (input/output/cacheRead/
  cacheWrite × unit price), period rollover, money/currency normalisation.
- **Adapters:** each provider adapter tested against fixture data (sample
  provider pricing payloads / canonical file samples); fallback-to-persisted
  behaviour on fetch failure; staleness signal emission.
- **Pricing file validation:** the canonical schema validator accepts well-formed
  files and rejects incomplete/inconsistent ones (missing required fields,
  mismatched `currency`/`unit`) without partial imports.
- **Repositories:** tested against a real Postgres instance (via Testcontainers
  or the §14 docker-compose stack), covering CRUD + the budget resolution query.

### 13.3 Integration tests

- **Orleans:** `Microsoft.Orleans.TestingHost` (in-process TestCluster) to
  exercise `UserBudgetGrain` + `PricingGrain` end-to-end: capture accrual,
  concurrent captures on the same caller id, pricing stream updates propagating
  to `PricingGrain` activations, period reset.
- **Tracker API:** `WebApplicationFactory<Program>` bootstrapping the API with
  a real Postgres (Testcontainers) and a stub JWKS/issuer so auth can be tested
  without a real IdP. Covers: auth failure paths, check (allow/deny/unknown
  model), capture (happy path, unknown model, idempotency via `requestId`,
  duplicate capture), `AllowNonBudgetedUsers` toggling.
- **Admin app:** **bUnit** for Blazor component tests (group CRUD flows, budget
  override form validation, auth-gated rendering); plus integration tests for
  the admin command handlers against a real Postgres.

### 13.4 Contract / conformance tests

- A small set of contract tests fix the gateway-facing API shape (request and
  response schemas for `/api/budget/check` and `/api/usage/capture`, error
  envelopes) so that an unintended breaking change is caught in CI.

## 14. Local development environment

Goal: `docker compose up -d` then **F5** in the IDE gives a fully debuggable
local setup — both the tracker API and the admin app — against real dependencies
(no mocks required for local dev).

### 14.1 `docker compose` stack (dependencies only)

The compose file brings up **only dependencies**; the two .NET apps are run from
the IDE/debugger so they can be stepped through. Services:

| Service | Image | Purpose | Notes |
| --- | --- | --- | --- |
| `postgres` | `postgres:17` | Primary data store | Pre-created DB + user; schema applied via EF migrations on app boot or a `migrate` step. Port 5432 exposed. |
| `otel-collector` | `otel/opentelemetry-collector-contrib` | Receives OTLP logs/metrics/traces from both apps | Forwards to Loki, Tempo, Prometheus. |
| `loki` | `grafana/loki` | Log backend | Fed by the collector. |
| `tempo` | `grafana/tempo` | Trace backend | Fed by the collector. |
| `prometheus` | `prom/prometheus` | Metrics backend | Scraped from the collector (or the apps directly). |
| `grafana` | `grafana/grafana` | Single UI for logs/traces/metrics | Pre-provisioned datasources (Loki/Tempo/Prometheus) and a starter dashboard for the tracker's metrics (§10.2). Port 3000 exposed. |
| `seq` *(optional, dev convenience)* | `datalust/seq` | Human-friendly Serilog viewer | The apps ship Serilog to Seq in dev in addition to OTLP, for fast log inspection. Port 8081 exposed. |

- All services are on a single compose network; the .NET apps reach them via
  `localhost` mapped ports (so IDE debugging works without joining the compose
  network).
- Grafana datasources and dashboards are provisioned via mounted config files
  (no manual UI setup) so the stack is reproducible.
- Postgres data is on a named volume so it survives teardown but can be wiped
  with `docker compose down -v`.

### 14.2 App bootstrapping in dev

- Both apps read `appsettings.Development.json` which points at the compose-hosted
  services (`localhost:5432`, `localhost:4317` for OTLP, `localhost:8081` for
  Seq, etc.).
- EF migrations apply automatically on the tracker's first boot in Development
  (or via a `dotnet ef database update` step — to be decided, see §15).
- The tracker's JWKS/issuer config in dev points at a local/stub issuer (e.g. a
  containerised stub IdP or a fixed test key) so the auth path can be exercised
  without EntraID. The admin app's EntraID config in dev may use a dedicated
  dev EntraID app registration or a local OIDC stub.

### 14.3 Developer workflow

1. `docker compose up -d` — brings up Postgres + the telemetry stack.
2. Open the solution in the IDE, hit **F5** on the **Tracker** launch profile
   → API + silo start, connect to compose Postgres, emit telemetry to the
   collector.
3. (Optionally) start a second debug session on the **Admin** launch profile →
   Blazor app boots, EntraID sign-in, talks to the same Postgres.
4. Hit the tracker with sample check/capture requests (a `curl`/`.http` file is
   provided in the repo); watch traces & logs in Grafana/Seq; watch spend accrue
   in the admin app.

## 15. Deferred / next-step topics

To be specified in a follow-up section of this document:

1. **Budget period** configurability (monthly default; weekly/quarterly/custom).
2. **Hierarchical budgeting (nested groups).** Groups may be nested; budgets can
   be assigned at any level of the hierarchy. In the hierarchical case, **every
   higher-level group has an active grain** tracking the budget remaining at that
   group level. When a caller's spend accrues, it is also decremented from each
   ancestor group's running grain. **When a higher-level group's budget is
   exhausted, the user grains under it are notified via an Orleans stream** so
   subsequent `check` calls deny fast without re-walking the hierarchy. This
   replaces (or extends) the flat per-caller model of iteration 1 and is a future
   iteration.
3. Staleness alerting thresholds and an explicit audit log query surface
   (metrics & traces are covered by §10.2; alert rules in Grafana are a
   follow-up).
4. **DB snapshotting & historicity.** The `usage_events` ledger (§9.4) is
   append-only and grows unbounded; a strategy for snapshots, compaction,
   archival, and point-in-time reconstruction of per-caller / per-group running
   spend is deferred to a future step.
5. Currency normalisation policy across providers (rates source, when to
   convert, how to store mixed-currency budgets).
6. EF migration application strategy in non-Development environments
   (auto-migrate on boot vs. dedicated `migrate` step in the deploy pipeline).
7. Production deployment topology (silo count, refresh-job singularity across
   replicas, leader election if needed).

## 16. Open questions summary

| # | Topic | Status |
| --- | --- | --- |
| Q1 | Token subject (gateway vs. end user) | **Resolved** — gateway principal; caller id supplied as field and trusted. |
| Q2 | No-budget caller behaviour | **Resolved** — fail-closed by default; configurable via `AllowNonBudgetedUsers` (default `false`). |
| Q3 | Pricing source strategy | **Resolved** — hybrid (live fetch + persisted fallback + staleness) plus manual pricing file upload via the Admin App. |
| Q4 | Pricing grain refresh mechanism | **Resolved** — Orleans streams (push); `PricingGrain` is a `[StatelessWorker]` local grain with multiple activations per silo. See §8.6. |
| Q5 | Grain-side invalidation of budget/group data after admin writes | **Resolved** — 30 s TTL; each `UserBudgetGrain` re-reads the effective budget from the DB at most every 30 s. Configurable via `BudgetCacheTtlSeconds` (default `30`). See §12.4. |
