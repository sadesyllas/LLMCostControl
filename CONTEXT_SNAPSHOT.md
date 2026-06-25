# Context Snapshot

Snapshot taken after completing milestone **M19**. All milestones are done.

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
| M19 | Contract/conformance tests + E2E local-dev verification | [x] | [x] |

**All milestones complete.**

## Test Counts (verified green)

| Test project | Tests | Notes |
|--------------|------:|-------|
| LLMCostControl.Domain.Tests | 38 | Windows |
| LLMCostControl.Infrastructure.Tests | 40 | WSL (Testcontainers) |
| LLMCostControl.Grains.Tests | 24 | Windows |
| LLMCostControl.Tracker.Api.Tests | 33 | Windows (6 auth + 7 endpoint + 1 smoke + 6 M14 + 4 M15 + 9 M19 contract) |
| LLMCostControl.Observability.Tests | 1 | Windows |
| LLMCostControl.Admin.App.Tests | 24 | Windows: 1 smoke + 5 auth + 5 bUnit M17 + 3 view + 5 upload; WSL: 5 integration |

**Total: 160 tests** (155 Windows + 5 WSL Testcontainers).

## Repository Layout

```
src/
  LLMCostControl.Domain/            # Domain model (value objects, entities)
  LLMCostControl.Infrastructure/    # EF Core, repositories, pricing adapters, refresh job
    Pricing/
      IPricingAdapter.cs, IPricingStoreWriter.cs, IPricingUpdatePublisher.cs
      IPricingImportService.cs, PricingImportService.cs, ModelPricingWriter.cs
      PricingImportResult.cs, PricingRefreshJob.cs, PricingFileValidator.cs
      PricingFile.cs, PricingUpdatedEvent.cs, PricingRefreshOptions.cs
  LLMCostControl.Grains.Abstractions/ # Grain interfaces, DTOs, stream events, exceptions
  LLMCostControl.Grains/            # Grain implementations, stores, publishers, options
  LLMCostControl.Observability/     # Serilog + OTel SDK wiring
  LLMCostControl.Tracker.Api/       # ASP.NET Core host: silo + auth + check/capture/import
    Endpoints/ApiDtos.cs, PricingImportDtos.cs
    Telemetry/TrackerMetrics.cs
  LLMCostControl.Admin.App/         # Blazor admin app
    Auth/AppRoles.cs
    Services/IGroupAdminService.cs, GroupAdminService.cs
    Services/IAdminReadService.cs, AdminReadService.cs
    Services/NullPricingPublisher.cs
    Components/Pages/Admin/Groups.razor, GroupDetailPanel.razor
    Components/Pages/Admin/Overrides.razor, PricingAdmin.razor
    Components/Pages/Reports.razor
tests/
  LLMCostControl.Domain.Tests/
  LLMCostControl.Infrastructure.Tests/
  LLMCostControl.Grains.Tests/
  LLMCostControl.Tracker.Api.Tests/
    ContractTests.cs (M19), PricingImportEndpointTests.cs (M14),
    EffectiveGroupTelemetryTests.cs (M15), CheckCaptureEndpointTests.cs (M13)
  LLMCostControl.Observability.Tests/
  LLMCostControl.Admin.App.Tests/
    Bunit/GroupCrudTests.cs, ReportsViewTests.cs, PricingUploadTests.cs
    Integration/AdminCommandHandlerTests.cs
docker/postgres/                    # init.sql, orleans-main.sql, orleans-persistence.sql
docker-compose.yml                  # postgres, otel-collector, loki, tempo, prometheus, grafana, seq
requests.http                       # VS Code REST Client manual probe file (M19)
```

## Key New Items (M14–M19)

### M14 — Localhost pricing file import
- `POST /api/pricing/import` (loopback-only, no auth, 422 on invalid)
- `IPricingStoreWriter` / `ModelPricingWriter` / `PricingImportService` / `IPricingImportService`
- `StubPricingStoreWriter` in Tracker.Api.Tests; 6 tests

### M15 — Effective-group telemetry tagging
- `TrackerMetrics` (OTel counter/histogram with budget_source + effective_group)
- `BudgetSource` + `EffectiveGroupId` added to `UsageCaptureResult`
- `Activity.Current?.SetTag(...)` + `ILogger.BeginScope(...)` in check/capture handlers
- 4 tests (ActivityListener + MeterListener)

### M16 — Blazor admin app auth
- Cookie + OpenIdConnect OIDC (EntraID) with configurable AzureAd section
- `AppRoles.Admin = "CostTracker.Admin"` / `AppRoles.ReadOnly = "CostTracker.ReadOnly"`
- `TestAuthHandler` bypasses OIDC via `X-Test-User` / `X-Test-Roles` headers
- 5 auth tests (WebApplicationFactory)

### M17 — Admin CRUD
- `IGroupAdminService` / `GroupAdminService` (groups, budgets, membership, overrides)
- `GroupMembershipRepository.GetMembershipsForGroupAsync` (added)
- `GroupBudgetRepository.DeleteAsync` (added)
- 5 bUnit (NSubstitute) + 5 Testcontainers integration tests

### M18 — Read-only views + pricing upload
- `IAdminReadService` / `AdminReadService` (caller budget summary, pricing, events)
- `NullPricingPublisher` (Admin.App has no Orleans; next refresh tick propagates)
- `Reports.razor` (caller lookup → budget/spend/events table)
- `PricingAdmin.razor` (pricing table with stale badges + JSON import textarea)
- 8 bUnit tests (3 view + 5 upload)

### M19 — Contract tests + manual probe
- 9 contract tests fixing check/capture request + response JSON schema and error envelope
- `requests.http` for manual §14.3 E2E walkthrough

## Toolchain (unchanged from M13 snapshot)

- **SDK:** .NET 11.0.100-preview.5.26302.115
- **Target framework:** `net11.0`
- **Key new packages:**
  - `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.0 (Admin.App)
  - `bunit` 2.7.2 (Admin.App.Tests)
