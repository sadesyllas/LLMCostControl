# Context Snapshot

Snapshot taken after completing milestone **M14**. This file is a quick-reference
for resuming work on **M15** and beyond.

## Project Status

| Milestone | Title | Done? | Tested? |
|-----------|-------|------:|--------:|
| M0 | Repo & solution scaffolding | [x] | [x] |
| M1 | Local dev dependencies (docker compose) | [x] | [x] |
| M2 | Observability skeleton (Serilog + OTel SDK) | [x] | [x] |
| M3 | Domain model | [x] | [x] |
| M4 | Infrastructure: EF Core + migrations + repositories | [x] | [x] |
| M5 | Pricing adapter abstraction + canonical file schema | [x] | [x] |
| M6 | Provider adapters (Google, OpenAI, Anthropic) | [x] | [x] |
| M7 | Pricing refresh job | [x] | [x] |
| M8 | Orleans silo host + grain interfaces + storage | [x] | [x] |
| M9 | PricingGrain ([StatelessWorker] + stream sub) | [x] | [x] |
| M10 | UserBudgetGrain: budget resolution + 30 s TTL | [x] | [x] |
| M11 | Cost accrual + usage audit trail | [x] | [x] |
| M12 | Auth: OAuth/JWKS validation | [x] | [x] |
| M13 | Tracker API: check + capture endpoints | [x] | [x] |
| M14 | Localhost pricing file import endpoint | [x] | [x] |
| M15 | Effective-group telemetry tagging | [ ] | [ ] |
| M16 | Blazor admin app: scaffolding + EntraID auth | [ ] | [ ] |
| M17 | Admin app: groups/budgets/membership/overrides CRUD | [ ] | [ ] |
| M18 | Admin app: read-only views + pricing file upload | [ ] | [ ] |
| M19 | Contract/conformance tests + E2E local-dev verification | [ ] | [ ] |

**Next milestone: M15** — Effective-group telemetry tagging (Spec ref §10.2,
depends on M10, M11, M2).

## Test Counts (verified green)

Total: **121 tests**, all passing.

| Test project | Tests |
|--------------|------:|
| LLMCostControl.Domain.Tests | 38 |
| LLMCostControl.Infrastructure.Tests | 40 |
| LLMCostControl.Grains.Tests | 24 |
| LLMCostControl.Tracker.Api.Tests | 17 (6 auth + 7 endpoint + 3 import + 1 smoke) |
| LLMCostControl.Observability.Tests | 1 |
| LLMCostControl.Admin.App.Tests | 1 |

> **Note:** `LLMCostControl.Infrastructure.Tests` (40) use **Testcontainers**
> Postgres and require a running Docker daemon. They cannot run in an
> environment without Docker; the M14 tests deliberately use stubs and need no
> Docker.

## Repository Layout

```
src/
  LLMCostControl.Domain/            # Domain model (value objects, entities)
  LLMCostControl.Infrastructure/    # EF Core, repositories, pricing adapters, refresh job
  LLMCostControl.Grains.Abstractions/ # Grain interfaces, DTOs, [GenerateSerializer] state, exceptions
  LLMCostControl.Grains/            # Grain implementations, stores, publishers, options
  LLMCostControl.Observability/     # Serilog + OTel SDK wiring
  LLMCostControl.Tracker.Api/       # ASP.NET Core host: silo + auth + check/capture endpoints
  LLMCostControl.Admin.App/         # Blazor admin (stub, M16+)
tests/
  LLMCostControl.Domain.Tests/
  LLMCostControl.Infrastructure.Tests/
  LLMCostControl.Grains.Tests/
  LLMCostControl.Tracker.Api.Tests/
  LLMCostControl.Observability.Tests/
  LLMCostControl.Admin.App.Tests/
docker/postgres/                    # init.sql, orleans-main.sql, orleans-persistence.sql
docker-compose.yml                  # postgres, otel-collector, loki, tempo, prometheus, grafana, seq
```

## Toolchain

- **SDK:** .NET 11.0.100-preview.5.26302.115 (`global.json`, `rollForward: latestFeature`)
- **Target framework:** `net11.0` (all projects)
- **Central Package Management:** `Directory.Packages.props`
- **`Directory.Build.props`:** `TreatWarningsAsErrors=true`, `NoWarn=NU1608`,
  `InvariantGlobalization=true`, `Nullable=enable`, `ImplicitUsings=enable`
- **`.editorconfig`:** file-scoped namespaces (warning), EF migrations exempt
- **Key package versions:**
  - Orleans 10.2.0 (Server, Persistence.AdoNet, Streaming, TestingHost)
  - EF Core 9.0.1 + Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4
  - Npgsql 9.0.3
  - Microsoft.AspNetCore.Authentication.JwtBearer 9.0.0
  - System.IdentityModel.Tokens.Jwt / Microsoft.IdentityModel.Tokens 8.3.0
  - Serilog 4.3.0 + sinks (Console, Seq, OpenTelemetry)
  - OpenTelemetry 1.16.0 exporters
  - Microsoft.Extensions.Hosting 10.0.5
  - xunit 2.9.3, FluentAssertions 7.2.0, NSubstitute 5.3.0,
    Microsoft.AspNetCore.Mvc.Testing 9.0.0, Testcontainers.PostgreSql 4.6.0

## Key Architectural Decisions

1. **`Money` and `TokenPrices` are `sealed record`** (not `readonly record struct`)
   for EF Core `ComplexProperty` compatibility.
2. **`PricingGrain` is `[StatelessWorker]`** with a shared `IPricingCache`
   (per-silo singleton, 30 s TTL). Stream subscriptions aren't allowed on
   `StatelessWorker` grains, so cache + store is used instead.
3. **Orleans 10.2.0 `[PersistentState]`** must be on a **constructor parameter**
   of type `IPersistentState<T>`, not on a field. Field-level `[PersistentState]`
   is not valid in Orleans 10.
4. **`OrleansPricingPublisher`** uses `IClusterClient` (not `IGrainFactory`) for
   stream access — `GetStreamProvider` is on `IClusterClient`/`Grain`.
5. **Shared static singletons in tests** (`SharedPricingStore`, `SharedBudgetStore`,
   `SharedUsageEventStore`) ensure test code and the silo DI container share the
   same store instance.
6. **`FakeTimeProvider`** (subclass of `TimeProvider`) is injected into
   `UserBudgetGrain` for deterministic TTL and period-rollover testing.
7. **`UseLocalhostClustering()`** is used in `Program.cs` when no Orleans storage
   connection string is present — needed for `WebApplicationFactory` tests to
   boot Orleans without a DB.
8. **`RequireHttpsMetadata = false`** on JWT Bearer for dev/test (mock OIDC
   server uses HTTP). `ClockSkew = 1 minute`.
9. **`GatewayAuthOptions.IsEnabled`** gates the entire auth pipeline: if false,
   `UseAuthentication`/`UseAuthorization` and `RequireAuthorization()` are
   skipped entirely.
10. **Service provider validation** (`ValidateScopes`, `ValidateOnBuild`) is
    disabled in test `WebApplicationFactory` instances to avoid Orleans
    membership table resolution failures.
11. **`BudgetGrainOptions`** is registered both as `IOptions<BudgetGrainOptions>`
    (config binding) and as a raw singleton (for grain constructor injection).
12. **`NU1608`** suppressed globally (Orleans source generator CodeAnalysis
    version conflict).
13. **EF Core pinned to 9.0.1** to match Npgsql.EntityFrameworkCore.PostgreSQL
    9.0.4 transitive dependency.
14. **Loki config:** `replication_factor: 1`, `allow_structured_metadata: true`,
    `user: root`. Collector logs→Loki via `otlphttp/loki` exporter (loki exporter
    removed in OTel collector 0.154.0).
15. **`Program.cs` is `public partial`** (required for `WebApplicationFactory<Program>`).
    Uses top-level statements with `app.Run()`.
16. **M14 import reuses the M7 write path via an `IPricingWriter` seam** rather
    than calling EF directly. `PricingImportService` validates the whole file
    atomically first (M5 validator) — no partial import — then writes + publishes
    per provider. The service lives in `Infrastructure` (not the API) so M18's
    admin upload shares the exact code path (§12.3).
17. **`PricingGrain` does NOT subscribe to the `pricing-updated` stream**
    (StatelessWorker can't — see #2). So imported prices become visible to grains
    via the store + 30 s cache TTL; the stream event is still published for the
    contract / future consumers. M14 tests assert grain visibility by reading the
    grain after import (same as M9), and assert publish via a recording publisher.
18. **Localhost-only enforcement** is an `IEndpointFilter` checking
    `Connection.RemoteIpAddress` (null or non-loopback → 404). Tests drive it over
    the in-memory TestServer via an `IStartupFilter` that sets `RemoteIpAddress`
    from an `X-Test-RemoteIp` header (the test server leaves it unset otherwise).

## Key Source Files

### Domain (`src/LLMCostControl.Domain/`)
- `Common/CallerId.cs`, `Common/Money.cs`, `Common/BudgetPeriod.cs`
- `Budgets/Group.cs`, `GroupMembership.cs`, `GroupBudget.cs`,
  `UserBudgetOverride.cs`, `BudgetSource.cs`, `EffectiveBudget.cs`
- `Pricing/ModelPricing.cs`, `TokenPrices.cs`, `Provider.cs`
- `Usage/UsageEvent.cs`

### Infrastructure (`src/LLMCostControl.Infrastructure/`)
- `Data/CostTrackerDbContext.cs` — EF context with `ComplexProperty` for value
  objects, `ValueConverter`s for `CallerId`/`BudgetPeriod`
- `Data/Factories/CostTrackerDbContextFactory.cs` — design-time factory
- `Data/Migrations/20260624152913_InitialCreate.cs`
- `Repositories/` — Group, GroupMembership, GroupBudget, UserBudgetOverride,
  ModelPricing, UsageEvent, BudgetResolution
- `Pricing/IPricingAdapter.cs`, `PricingFile.cs`, `PricingFileValidator.cs`
- `Pricing/Adapters/PricingAdapterBase.cs` (hybrid fallback + staleness),
  `OpenAIPricingAdapter.cs`, `AnthropicPricingAdapter.cs`, `GooglePricingAdapter.cs`
- `Pricing/PricingRefreshJob.cs` (BackgroundService, singularity guard,
  per-provider cadence + jitter), `PricingRefreshOptions.cs`,
  `PricingUpdatedEvent.cs`, `IPricingUpdatePublisher.cs`
- `Pricing/IPricingWriter.cs` / `DbPricingWriter.cs` — write seam over
  `ModelPricingRepository.ReplaceProviderPricingAsync` (M7 write path), uses
  `IDbContextFactory` (singleton-safe, like `PricingStore`)
- `Pricing/PricingImportService.cs` (+ `PricingImportResult`) — M14/§8.3 shared
  import pipeline: validate (M5) → write per provider (`IPricingWriter`) →
  publish per provider. Lives in Infrastructure so M18's admin upload reuses it

### Grains (`src/LLMCostControl.Grains*/`)
- `Abstractions/IUserBudgetGrain.cs`, `IPricingGrain.cs`
- `Abstractions/Dto/BudgetCheckResult.cs`, `UsageCapture.cs`
- `Abstractions/StreamEvents/PricingUpdatedStreamEvent.cs`
- `Abstractions/Exceptions/UnknownModelException.cs` (`[GenerateSerializer]`)
- `Abstractions/State/UserBudgetGrainState.cs` (`[GenerateSerializer]`)
- `Grains/Implementations/PricingGrain.cs` — `[StatelessWorker]` + cache
- `Grains/Implementations/UserBudgetGrain.cs` — budget resolution, TTL cache,
  persistent state, cost accrual, audit row, idempotency, unknown-model
  rejection, period rollover
- `Grains/Storage/PricingStore.cs` (`IPricingStore`), `PricingCache.cs`
  (`IPricingCache`), `IBudgetStore.cs`/`BudgetStore`, `IUsageEventStore.cs`/
  `UsageEventStore`
- `Grains/Publishers/OrleansPricingPublisher.cs`
- `Grains/Options/BudgetGrainOptions.cs` — `BudgetCacheTtl` (30 s),
  `AllowNonBudgetedUsers` (false)

### Tracker API (`src/LLMCostControl.Tracker.Api/`)
- `Program.cs` — Orleans silo config, JWT Bearer auth, `/api/budget/check`,
  `/api/usage/capture`, `/api/auth/test`, `/api/pricing/import` (M14, localhost
  only, no gateway token); registers `IPricingWriter`→`DbPricingWriter` and
  `PricingImportService`
- `Auth/GatewayAuthOptions.cs` — JwksEndpoint, Issuer, Audience, IsEnabled
- `Endpoints/ApiDtos.cs` — request/response DTOs for check + capture + import
  (`PricingImportResponse`, `PricingImportErrorResponse`)
- `Endpoints/LocalhostOnlyEndpointFilter.cs` — `IEndpointFilter` returning 404
  for non-loopback `Connection.RemoteIpAddress` (M14, §8.3)

### Observability (`src/LLMCostControl.Observability/`)
- `ObservabilityExtensions.cs` — Serilog + OTel SDK wiring (logs→Loki,
  traces→Tempo, metrics→Prometheus)

## Key Test Files

- `Grains.Tests/GrainTestBase.cs` — `GrainClusterFixture`, shared static
  singletons, `TestSiloConfigurator`
- `Grains.Tests/FakeTimeProvider.cs` — `TimeProvider` subclass
- `Grains.Tests/Stubs/` — `StubPricingStore.cs`, `StubBudgetStore.cs`,
  `StubUsageEventStore.cs` (with call counting)
- `Grains.Tests/PricingGrainTests.cs` (5 tests)
- `Grains.Tests/UserBudgetGrainTests.cs` (15 tests: M10 budget resolution +
  M11 cost accrual)
- `Grains.Tests/SiloBootstrapTests.cs` (4 tests)
- `Tracker.Api.Tests/JwtTestHelper.cs` — RSA key gen, JWT issuance, JWKS JSON
- `Tracker.Api.Tests/MockOidcServer.cs` — HttpListener OIDC discovery + JWKS
- `Tracker.Api.Tests/GatewayAuthTests.cs` — 6 auth tests +
  `AuthWebAppFactory`
- `Tracker.Api.Tests/CheckCaptureEndpointTests.cs` — 7 endpoint tests +
  `TrackerApiFactory`
- `Tracker.Api.Tests/PricingImportEndpointTests.cs` — 3 M14 tests
  (valid→persist+publish+grain-visible, invalid→400+no-write, non-localhost→404)
  + `PricingImportApiFactory`, `StubPricingWriter`, `RecordingPricingPublisher`,
  `TestRemoteIpStartupFilter` (middleware setting `RemoteIpAddress` from a header)
- `Tracker.Api.Tests/TestStubs.cs` — simpler stub stores for API integration
  (`StubPricingStore` now has `ReplaceProvider`/`Clear`/`Count` for import tests)
- `Tracker.Api.Tests/SmokeTests.cs` — 1 smoke test
- `Infrastructure.Tests/RepositoryTestBase.cs` — Testcontainers Postgres
- `Infrastructure.Tests/Stubs/StubPricingComponents.cs`

## Test Conventions

- Grain tests use `IClassFixture<GrainClusterFixture>` with
  `[CollectionBehavior(CollectionPerAssembly)]` +
  `DisableTestParallelization = true` — all grain tests run sequentially in one
  collection.
- `TrackerApiFactory` (in `CheckCaptureEndpointTests.cs`) and
  `AuthWebAppFactory` (in `GatewayAuthTests.cs`) are separate
  `WebApplicationFactory<Program>` subclasses that override `CreateHost` to
  inject mock OIDC config and disable service provider validation.
- Test stub stores exist in two places: `tests/LLMCostControl.Grains.Tests/Stubs/`
  (with call counting) and `tests/LLMCostControl.Tracker.Api.Tests/TestStubs.cs`
  (simpler, for API integration tests).

## Git History (recent)

```
ebd9ee8 M14: mark milestone done and tested
773bf0d M14: implement localhost pricing file import endpoint
fea1c21 docs: add CONTEXT_SNAPSHOT.md after M13
01172f4 M13: mark milestone done and tested
2ebf893 M13: implement check + capture endpoints with auth, DTOs, error handling...
ecc3e65 M12: mark milestone done and tested
89f6cff M12: implement gateway OAuth/JWKS validation...
d9faf0e M11: mark milestone done and tested
7845232 M11: implement cost accrual + usage audit trail...
569f53f M10: mark milestone done and tested
b8c1efd M10: implement UserBudgetGrain...
80b192f M9: mark milestone done and tested
517329b M9: implement PricingGrain...
0c68a7e M8: mark milestone done and tested
...
```

## Rules of Engagement (from AGENTS.md)

1. Adhere to `SPEC.md` — surface conflicts, don't improvise.
2. Follow `MILESTONES.md` strictly in order.
3. A milestone is complete only when both `Done?` and `Tested?` are `[x]`.
4. No untested code advances to the next milestone.
5. Commit at every step with milestone-referenced messages (e.g. `M14: ...`).
6. Document all types and public methods with XML `///` comments (production
   code only; tests exempt).
