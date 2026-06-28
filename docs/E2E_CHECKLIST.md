# E2E local-dev verification checklist (§14.3)

Manual end-to-end sign-off for the LLM Cost Tracker. It exercises the full §14.3
developer workflow against the real local dependency stack. The automated
contract suite (`ContractTests`, §13.4) locks the gateway-facing API shape in CI;
this checklist covers the parts that need a human to observe (IDE F5, Grafana
dashboards, the admin UI).

> **Environment note.** Docker isn't installed on the Windows host in this setup;
> the dependency stack and the Testcontainers tests run under **WSL** (Docker Hub
> & nuget.org are firewalled there). The IDE F5 steps assume the .NET apps reach
> the WSL-hosted services via `localhost` mapped ports. Exact ports are in each
> app's `Properties/launchSettings.json`.

## Prerequisites

- [ ] `docker compose up -d` brings up postgres, otel-collector, loki, tempo,
      prometheus, grafana, and (optional) seq.
- [ ] All services report healthy (`docker compose ps`).
- [ ] Grafana reachable at <http://localhost:3000> with Loki/Tempo/Prometheus
      datasources connected; Seq (optional) at <http://localhost:8081>.
- [ ] `Observability:TelemetryPepper` is configured for both apps (the host
      **fails fast** on startup if it is missing — §10.2). Dev values are in each
      app's `appsettings.Development.json`.

## 1. Tracker API + Orleans silo (F5)

- [ ] Run the **Tracker** launch profile (see `LLMCostControl.Tracker.Api`
      `launchSettings.json` for the URL).
- [ ] EF migrations apply on first boot; the silo connects to the compose Postgres.
- [ ] Startup logs appear in the console **and** in Grafana/Loki (and Seq).

## 2. Admin app (F5, optional second session)

- [ ] Run the **Admin** launch profile (see `LLMCostControl.Admin.App`
      `launchSettings.json` for the URL).
- [ ] EntraID sign-in completes (or the configured dev OIDC stub); an anonymous
      request is redirected to the identity provider (see `OidcSignInTests`).
- [ ] An admin (`CostTracker.Admin`) sees the management + **Pricing** pages in
      the nav; a read-only user (`CostTracker.ReadOnly`) sees only the read views.

## 3. Seed pricing + a budget

- [ ] Upload a canonical pricing file (§8.5) via the Admin **Pricing** page —
      confirm the success message and the row count imported. (Manual pricing
      upload is **Admin-App only**, §8.3; there is no localhost import endpoint.)
- [ ] The pricing file is **per provider**: confirm models appear under their
      provider (e.g. `gpt-4o` under `openai`, `claude-3-5-sonnet` under
      `anthropic`) on the Pricing page, each with `fetchedAt` + a fresh/stale badge.
- [ ] In the management page: create a group, set a budget for the current period,
      and add `alice@example.com` as a member.

## 4. Issue check + capture (`requests.http`)

- [ ] `POST /api/budget/check` for `alice@example.com` → `allowed: true` with the
      effective budget, running spend, and remaining.
- [ ] `POST /api/usage/capture` **without** a `provider` field → the provider is
      inferred from the model name (§6.2.3) and a non-zero `cost` is returned with
      updated `runningSpend` / `remaining`.
- [ ] `POST /api/usage/capture` **with** an explicit `provider` → priced against
      that exact (provider, model) pair.
- [ ] Re-send capture with the **same** `requestId` → running spend does **not**
      change (idempotency).
- [ ] `POST /api/usage/capture` with an unknown / unresolvable model →
      `400 { "error": "unknown_model" }` (never priced at zero).

## 5. Provider-aware pricing (per §8.1)

- [ ] If the same model name exists under two providers in the pricing file, a
      capture specifying each provider is priced against that provider's rates.
- [ ] Setting a group's budget to **0** cuts off that group's members on the next
      check (within the ~30 s budget cache TTL) — `allowed: false`.

## 6. Observe telemetry (§10.2)

- [ ] In Grafana/Tempo: a trace for the check/capture request carries the
      `effective_group` and `budget_source` attributes.
- [ ] The `caller_id` attribute is an **HMAC-SHA256 hash**, not the raw email
      (PII protection, §10.2).
- [ ] In Grafana/Loki (or Seq): the decision log events carry the same
      `effective_group` / `budget_source` properties.
- [ ] In Grafana/Prometheus: the budget-check / usage-capture / capture-cost
      metrics are present, sliceable by those tags.

## 7. Observe spend in the Admin app

- [ ] Admin reports → look up `alice@example.com`: the effective budget, running
      spend, and remaining reflect the captures just issued; the recent usage list
      shows the capture rows.

## Sign-off

- [ ] All boxes above checked. Date / initials: ____________________
