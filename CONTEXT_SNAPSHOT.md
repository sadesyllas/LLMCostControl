# Context Snapshot

Snapshot taken after completing milestone **M18**. This file is a quick-reference
for resuming work on **M19** and beyond.

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
| M15 | Effective-group telemetry tagging | [x] | [x] |
| M16 | Blazor admin app: scaffolding + EntraID auth | [x] | [x] |
| M17 | Admin app: groups/budgets/membership/overrides CRUD | [x] | [x] |
| M18 | Admin app: read-only views + pricing file upload | [x] | [x] |
| M19 | Contract/conformance tests + E2E local-dev verification | [ ] | [ ] |

**Next milestone: M19** — Contract/conformance tests + E2E local-dev verification
(Spec ref §13.4, §14.3, depends on M13, M14, M15, M18).

## Test Counts (verified green)

Total: **148 tests**, all passing (entire suite verified green in WSL, including
both Docker-gated Testcontainers suites).

| Test project | Tests |
|--------------|------:|
| LLMCostControl.Domain.Tests | 38 |
| LLMCostControl.Infrastructure.Tests | 40 (Testcontainers) |
| LLMCostControl.Grains.Tests | 24 |
| LLMCostControl.Tracker.Api.Tests | 21 (6 auth + 7 endpoint + 3 import + 4 telemetry + 1 smoke) |
| LLMCostControl.Observability.Tests | 1 |
| LLMCostControl.Admin.App.Tests | 24 (4 auth-gated + 1 OIDC + 6 admin CRUD bUnit + 6 CRUD Postgres + 2 reports bUnit + 2 upload bUnit + 2 query/import Postgres + 1 smoke) |

> **Docker-gated tests run in WSL.** Docker isn't installed on the Windows host,
> and in WSL both Docker Hub and nuget.org are firewalled. Recipe (see the
> `wsl-test-recipe` memory): `NUGET_PACKAGES=/mnt/c/Users/s.desyllas/.nuget/packages`
> (Windows cache, offline restore) + `TESTCONTAINERS_RYUK_DISABLED=true`
> (only `postgres:17` is `docker load`-ed) + `-p:NuGetAudit=false` (skips the
> api.nuget.org vuln fetch that `TreatWarningsAsErrors` turns fatal).
> `dotnet test LLMCostControl.slnx` with these passes all 142.

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
  - Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0 (bumped in M16 — required
    by Microsoft.Identity.Web 4.11.0; works on the net11 host)
  - Microsoft.Identity.Web 4.11.0 (M16, EntraID OIDC for the admin app)
  - System.IdentityModel.Tokens.Jwt / Microsoft.IdentityModel.Tokens 8.19.1
    (bumped in M16 from 8.3.0 to satisfy Microsoft.Identity.Web)
  - bunit 2.7.2 (Blazor component tests; **v2 API**: `Render<T>()` not
    `RenderComponent<T>()`; `AddAuthorization()` returns `BunitAuthorizationContext`)
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
19. **M15 effective-group tags are applied at the API layer, not the grain.** The
    grain resolves `effective_group`/`budget_source` at decision time and returns
    them on `BudgetCheckResult`/`UsageCaptureResult`; the endpoint then tags
    `Activity.Current` (the request span), records the metric with those tags, and
    pushes them into the Serilog `LogContext` for the request — because the
    request's activity/log context lives at the API layer, not in the silo. Tag
    value `effective_group = "none"` (sentinel) when there is no group so the tag
    is always present and sliceable. Keys: `effective_group`, `budget_source`.
20. **M16 admin auth uses Microsoft.Identity.Web (EntraID OIDC).** This forced two
    solution-wide package bumps: `Microsoft.AspNetCore.Authentication.JwtBearer`
    9.0.0→10.0.0 and `Microsoft.IdentityModel.*` 8.3.0→8.19.1. Tracker.Api auth
    (M12) re-verified green afterward. Auth is gated on `AzureAd:ClientId` so the
    app still boots without EntraID. Role gating in markup uses
    `<AuthorizeView Roles=...>` (not Policy) because bUnit's `SetRoles` maps to it
    cleanly. Auth enforced per-page via `[Authorize]` + `AuthorizeRouteView` (NOT a
    global FallbackPolicy — that would block static assets / the login endpoint).
21. **WebApplicationFactory config-before-Program gotcha (M16 OIDC test).** Program
    reads `AzureAd:ClientId` at top level *before* `ConfigureWebHost` app-config is
    applied, so test config must be injected via an overridden `CreateHost` +
    `ConfigureHostConfiguration` (same pattern as `TrackerApiFactory`). OIDC
    discovery is stubbed with `StaticConfigurationManager<OpenIdConnectConfiguration>`
    via `PostConfigure<OpenIdConnectOptions>` (no network, no live IdP).
22. **M16 deferred to M17:** the admin app references Infrastructure and registers
    `IDbContextFactory<CostTrackerDbContext>` (the shared-repo data path, no grains),
    but actual repository registration + CRUD pages land in M17.
23. **M17 admin commands go through `IAdminCommandService`** (real impl over the
    shared repos via `IDbContextFactory`; a `FakeAdminCommandService` powers bUnit
    UI tests; integration tests hit the real one against Testcontainers Postgres).
    Validation lives in the service (domain `Create` guards + amount > 0 + duplicate
    check); the page catches and displays errors. `bunit 2.x`: `Render<T>()`,
    `Find("[data-testid=x]").Change(...)/.Click()`. `@rendermode InteractiveServer`
    on the page does NOT break direct bUnit rendering.
24. **Docker tests run in WSL, not on the Windows host** (Docker isn't installed on
    Windows). In WSL both Docker Hub and nuget.org are firewalled — see the
    `wsl-test-recipe` memory and the Test Counts note for the exact env recipe.
    The full 148-test suite is verified green via `dotnet test LLMCostControl.slnx`
    in WSL with that recipe.
25. **M18 pricing upload reuses the shared `PricingImportService` (M14) with a
    no-op publisher.** The admin app has no Orleans/cluster client, so it cannot
    publish the `pricing-updated` stream event (§12.3); it writes via
    `DbPricingWriter` and pricing grains pick up the change on their next store
    reload. Read-only views go through `IAdminQueryService` (real over shared
    repos; `FakeAdminQueryService` for bUnit). Effective-budget resolution
    (largest-of-groups) is verified for real in the Postgres integration test.

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
- `Telemetry/TrackerTelemetry.cs` — M15/§10.2: owns the custom `Meter`
  (`tracker.budget_checks`, `tracker.usage_captures`, `tracker.capture_cost`) +
  `EnterEffectiveGroupScope` (tags `Activity.Current` + pushes Serilog
  `LogContext` props `effective_group`/`budget_source`). `ServiceName` const is
  the meter/source/OTel service name used in `Program.cs`

### Observability (`src/LLMCostControl.Observability/`)
- `ObservabilityExtensions.cs` — Serilog + OTel SDK wiring (logs→Loki,
  traces→Tempo, metrics→Prometheus). `UseObservability` also adds any
  DI-registered `Serilog.Core.ILogEventSink` (M15: lets tests capture log
  events in-memory; no-op in prod)

### Admin App (`src/LLMCostControl.Admin.App/`) — Blazor Web App (Interactive Server)
- `Program.cs` — EntraID OIDC via `AddMicrosoftIdentityWebApp("AzureAd")` (gated
  on `AzureAd:ClientId` so it boots without EntraID in dev/tests); role policies;
  `AddCascadingAuthenticationState`; minimal `/authentication/login` (Challenge) +
  `/authentication/logout` (SignOut) endpoints; conditional
  `AddDbContextFactory<CostTrackerDbContext>` (shared repos, **no Orleans/grains**,
  §12.1). Ends with `public partial class Program;` for `WebApplicationFactory`
- `Auth/AdminAuthorization.cs` — role names (`CostTracker.Admin`/`.ReadOnly`) +
  policy names (`AdminPolicy`/`ReadOnlyPolicy`)
- `Components/Auth/RedirectToLogin.razor` (navigates to login),
  `LoginDisplay.razor` (user + sign-out form)
- `Components/Routes.razor` — `AuthorizeRouteView` + `NotAuthorized`→`RedirectToLogin`
- `Components/Layout/NavMenu.razor` — `<AuthorizeView Roles=...>` gates "Reports"
  (any role) and "Administration" (`data-testid=admin-nav-link`, Admin only).
  Demo Counter/Weather pages removed
- `Home.razor` has `@attribute [Authorize]`; `appsettings.json` has an `AzureAd`
  section (empty placeholders; secret via env/user-secrets)
- `Services/IAdminCommandService.cs` + `AdminCommandService.cs` (M17) — admin CRUD
  handlers (groups/budgets/membership/overrides) over the shared repos via
  `IDbContextFactory` (new context per op; never grains). Validates: empty name,
  amount ≤ 0, empty caller, duplicate membership. Registered scoped in `Program.cs`
- `Components/Pages/Administration.razor` (M17) — `@page "/administration"`,
  `[Authorize(Policy=AdminPolicy)]`, `@rendermode InteractiveServer`. Full CRUD UI
  with `data-testid` hooks; errors surfaced via a `GuardAsync` try/catch
- Infrastructure repo additions (M17): `GroupBudgetRepository.DeleteAsync`
  (clear), `GroupMembershipRepository.GetCallersForGroupAsync` + `ExistsAsync`
- `Services/IAdminQueryService.cs` + `AdminQueryService.cs` (M18) — read-only:
  caller effective-budget+spend (`BudgetResolutionRepository` + usage sum),
  current pricing (+staleness), recent usage. `Services/CallerBudgetSummary.cs` DTO
- `Services/NoOpPricingUpdatePublisher.cs` (M18) — admin app has no Orleans, so
  the shared `PricingImportService` is wired with this no-op publisher; grains
  pick up uploads via their store-reload TTL (§12.3)
- `Components/Pages/Reports.razor` (M18, `/reports`, ReadOnlyPolicy) — caller
  budget/spend/usage + pricing table with stale/fresh badges
- `Components/Pages/PricingManagement.razor` (M18, `/pricing`, AdminPolicy) —
  textarea upload → shared `PricingImportService.ImportAsync`

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
- `Tracker.Api.Tests/EffectiveGroupTelemetryTests.cs` — 4 M15 tests
  (check for Group/UserOverride/None + a capture) asserting span+metric+log all
  carry `effective_group`/`budget_source`. `TelemetryApiFactory` wires in-memory
  OTel exporters (`ConfigureOpenTelemetryTracerProvider`/`...MeterProvider` +
  `AddInMemoryExporter`) and an `InMemoryLogSink` (`ILogEventSink`)
- `Tracker.Api.Tests/AssemblyInfo.cs` — `DisableTestParallelization = true`
  (in-memory OTel/Serilog capture is process-global; classes must not overlap)
- `Tracker.Api.Tests/TestStubs.cs` — simpler stub stores for API integration
  (`StubPricingStore` now has `ReplaceProvider`/`Clear`/`Count` for import tests)
- `Tracker.Api.Tests/SmokeTests.cs` — 1 smoke test
- `Admin.App.Tests/AuthGatedRenderingTests.cs` — 4 bUnit tests (admin/readonly/
  anonymous nav gating + RedirectToLogin). Uses `AddAuthorization()` +
  `BunitAuthorizationContext.SetAuthorized/SetRoles/SetNotAuthorized`, `Render<T>()`
- `Admin.App.Tests/OidcSignInTests.cs` — 1 integration test: GET
  `/authentication/login` → 302 to a stubbed OIDC authorize endpoint.
  `AdminAppFactory` injects `AzureAd:*` via `CreateHost`+`ConfigureHostConfiguration`
  (so Program reads it at top level) and stubs OIDC metadata via
  `PostConfigure<OpenIdConnectOptions>` + `StaticConfigurationManager`
- `Admin.App.Tests/AdministrationPageTests.cs` — 6 bUnit tests (full CRUD flow +
  validation: empty name, zero/negative budget, empty caller, duplicate member)
  using `FakeAdminCommandService` (in-memory `IAdminCommandService`)
- `Admin.App.Tests/AdminCommandServiceIntegrationTests.cs` — 6 Testcontainers
  Postgres tests over the real `AdminCommandService` (own `TestDbContextFactory`)
- `Admin.App.Tests/FakeAdminCommandService.cs` — in-memory service mirroring the
  real validation (for bUnit)
- `Admin.App.Tests/ReportsPageTests.cs` — 2 bUnit (staleness badge, caller summary);
  `PricingUploadPageTests.cs` — 2 bUnit (valid import / invalid errors via real
  `PricingImportService` + `RecordingPricingWriter`)
- `Admin.App.Tests/AdminQueryAndPricingIntegrationTests.cs` — 2 Testcontainers
  (caller-in-two-groups → largest budget + spend/remaining; real import persists /
  invalid writes nothing). `FakeAdminQueryService` + `RecordingPricingWriter` in
  `FakeAdminQueryService.cs`; shared `TestDbContextFactory.cs`
- `Admin.App.Tests/SmokeTests.cs` — 1 smoke test
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
b6fab47 M18: mark milestone done and tested
37df718 M18: admin read-only views + pricing file upload
2d54118 docs: update CONTEXT_SNAPSHOT.md after M17
2cffc5e M17: mark milestone done and tested
1eeb42f M17: admin CRUD for groups/budgets/membership/overrides
58acc38 docs: update CONTEXT_SNAPSHOT.md after M16
670b119 M16: mark milestone done and tested
b354a47 M16: Blazor admin app scaffolding + EntraID auth
b9043d7 docs: update CONTEXT_SNAPSHOT.md after M15
d1c0812 M15: mark milestone done and tested
5ccb718 M15: effective-group telemetry tagging on check/capture
392fd5d docs: update CONTEXT_SNAPSHOT.md after M14
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
