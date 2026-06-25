# E2E local-dev verification checklist (§14.3)

This is the manual end-to-end sign-off for the LLM Cost Tracker. It exercises the
full §14.3 developer workflow against the real local dependency stack. The
automated contract suite (`ContractTests`, §13.4) locks the gateway-facing API
shape in CI; this checklist covers the parts that need a human to observe
(IDE F5, Grafana dashboards, the admin UI).

> **Environment note.** Docker isn't installed on the Windows host in this
> setup; the dependency stack and the Testcontainers tests run under **WSL**
> (Docker Hub & nuget.org are firewalled there — see the `wsl-test-recipe`
> memory / `CONTEXT_SNAPSHOT.md`). The IDE F5 steps below assume the .NET apps
> reach the WSL-hosted services via `localhost` mapped ports.

## Prerequisites

- [ ] `docker compose up -d` brings up postgres, otel-collector, loki, tempo,
      prometheus, grafana, and (optional) seq.
- [ ] All services report healthy (`docker compose ps`).
- [ ] Grafana reachable at <http://localhost:3000> with Loki/Tempo/Prometheus
      datasources connected; Seq (optional) at <http://localhost:8081>.

## 1. Tracker API + Orleans silo (F5)

- [ ] Run the **Tracker** launch profile (`http` → <http://localhost:5196>).
- [ ] EF migrations apply on first boot; the silo connects to the compose Postgres.
- [ ] Startup logs appear in the console **and** in Grafana/Loki (and Seq).

## 2. Admin app (F5, optional second session)

- [ ] Run the **Admin** launch profile (<http://localhost:5280>).
- [ ] EntraID sign-in completes (or the configured dev OIDC stub).
- [ ] An admin (`CostTracker.Admin`) sees **Administration** + **Pricing** in the
      nav; a read-only user (`CostTracker.ReadOnly`) sees only **Reports**.

## 3. Seed pricing + a budget

- [ ] Import pricing via the localhost endpoint (request 3 in `requests.http`)
      **or** the Admin **Pricing** page — confirm a success response / message.
- [ ] In the Admin **Administration** page: create a group, set a budget for the
      current period, and add `alice@example.com` as a member.

## 4. Issue check + capture

- [ ] `POST /api/budget/check` for `alice@example.com` (request 1) → `allowed: true`
      with the effective budget, running spend, and remaining.
- [ ] `POST /api/usage/capture` (request 2) → a computed `cost`, updated
      `runningSpend`, and `remaining`.
- [ ] Re-send capture with the **same** `requestId` → running spend does **not**
      change (idempotency).
- [ ] `POST /api/usage/capture` with an unknown model → `400 { "error":
      "unknown_model" }`.

## 5. Observe telemetry (§10.2)

- [ ] In Grafana/Tempo: a trace for the check/capture request carries the
      `effective_group` and `budget_source` attributes.
- [ ] In Grafana/Loki (or Seq): the decision log events carry the same
      `effective_group` / `budget_source` properties.
- [ ] In Grafana/Prometheus: the `tracker.budget_checks` / `tracker.usage_captures`
      / `tracker.capture_cost` metrics are present, sliceable by those tags.

## 6. Observe spend in the Admin app

- [ ] Admin **Reports** → look up `alice@example.com`: the effective budget,
      running spend, and remaining reflect the captures just issued; the recent
      usage list shows the capture rows.
- [ ] Pricing table shows the imported model with `fetchedAt` and a fresh/stale badge.

## Sign-off

- [ ] All boxes above checked. Date / initials: ____________________
