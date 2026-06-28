# TODO

Operational follow-ups that are intentionally **not** in `SPEC.md` — they concern
verification and tooling, not the product specification. Tracked here so they
aren't lost.

## 1. Verify the hand-written `usage_events.provider` migration

- **Context:** the `provider` column on `usage_events` (§9.4, commit `d5cb835`)
  was added with a **hand-written** EF Core migration
  (`20260628000000_AddProviderToUsageEvents`). `dotnet-ef` could not be used in
  the current dev environment (tool not installed; nuget.org firewalled), so the
  migration's `Up()` was written by hand and the model snapshot updated manually.
  The `Up()` is a simple `AddColumn`, but it has **not** yet been applied against
  a real Postgres.
- **Action:**
  - Under **WSL** (Docker), run the Testcontainers-backed suites that apply
    migrations via `MigrateAsync` and confirm they pass:
    - `tests/LLMCostControl.Infrastructure.Tests`
    - `tests/LLMCostControl.Admin.App.Tests`
  - Optionally regenerate the migration with the real tooling so the
    `.Designer.cs` + snapshot are canonical: `dotnet ef migrations remove` then
    `dotnet ef migrations add AddProviderToUsageEvents` (in WSL, with `dotnet-ef`
    installed), and diff against the hand-written version.

## 2. Run the Docker / Testcontainers suites under WSL

- **Context:** Docker Hub & nuget.org are firewalled on the Windows host, so the
  Postgres/Testcontainers-backed suites cannot run there. The offline suites are
  green: `Domain.Tests`, `Grains.Tests`, `Observability.Tests`, and the Admin
  OIDC sign-in test.
- **Action:** under WSL (Windows NuGet cache + audit off, per the team's WSL test
  recipe), run the full suite and confirm green — especially the real-Postgres
  ones:
  - `LLMCostControl.Infrastructure.Tests` (repositories, refresh job)
  - `LLMCostControl.Admin.App.Tests` (CRUD, read views, pricing upload)
  - `LLMCostControl.Tracker.Api.Tests` (check / capture / contract)
