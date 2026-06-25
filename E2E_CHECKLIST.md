# E2E Local-Dev Verification Checklist

**Milestone M19** — Manual verification of the full §14.3 developer workflow.

This checklist documents the end-to-end local development workflow. It should
be signed off (all items checked) before declaring M19 complete.

## Prerequisites

- [ ] Docker Desktop (or equivalent) is running
- [ ] .NET 11 SDK is installed (`dotnet --version` shows `11.0.x`)
- [ ] The repository is cloned and on the `main` branch

## Step 1: Bring up dependencies

```bash
docker compose up -d
```

- [ ] All 7 containers are healthy: `postgres`, `otel-collector`, `loki`,
      `tempo`, `prometheus`, `grafana`, `seq`
- [ ] `docker compose ps` shows all services running
- [ ] Postgres is reachable on `localhost:5432`
- [ ] Grafana is reachable at `http://localhost:3000` (admin/admin)

## Step 2: Start the Tracker API

1. Open `LLMCostControl.slnx` in the IDE
2. Set `LLMCostControl.Tracker.Api` as the startup project
3. Verify `appsettings.Development.json` points at compose-hosted services
   (`localhost:5432`, `localhost:4317`)
4. Press **F5**

- [ ] The tracker API starts without errors
- [ ] EF migrations are applied to the Postgres database
- [ ] The Orleans silo boots and connects to Postgres for persistence
- [ ] Logs appear in the console (Serilog) and in Seq (`http://localhost:8081`)
- [ ] Traces are exported to the OTel collector (visible in Grafana → Tempo)
- [ ] Metrics are exported to the OTel collector (visible in Grafana → Prometheus)
- [ ] `GET /` returns `LLMCostControl Tracker API`

## Step 3: Start the Admin App (optional)

1. Set `LLMCostControl.Admin.App` as a secondary startup project
2. Press **F5** (or run in a second debug session)

- [ ] The Blazor admin app starts without errors
- [ ] If EntraID auth is disabled (`EntraId:IsEnabled=false` in
      `appsettings.Development.json`), the home page loads without redirect
- [ ] The Groups page is accessible at `/groups`
- [ ] The Pricing page is accessible at `/pricing`

## Step 4: Issue sample check/capture requests

Use the `requests.http` file in the repo root with an HTTP client (VS Code REST
Client, JetBrains HTTP Client, or curl).

### Budget Check

```bash
curl -X POST https://localhost:5001/api/budget/check \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"callerId":"user@example.com"}'
```

- [ ] Response is `200 OK` with `allowed`, `callerId`, `effectiveBudget`,
      `runningSpend`, and `remaining` fields
- [ ] An unbudgeted caller returns `allowed: false`
- [ ] A request without auth returns `401 Unauthorized`

### Usage Capture

```bash
curl -X POST https://localhost:5001/api/usage/capture \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"callerId":"user@example.com","model":"gpt-4o","tokens":{"input":1000,"output":500,"cacheRead":0,"cacheWrite":0},"requestId":"req-001"}'
```

- [ ] Response is `200 OK` with `callerId`, `cost`, `runningSpend`, and
      `remaining` fields
- [ ] The cost is non-zero and correctly computed (tokens × price / 1M)
- [ ] A duplicate `requestId` returns the same result (idempotent — no
      double accrual)
- [ ] An unknown model returns `400` with `{"error":"unknown_model",...}`

### Pricing File Import (localhost only)

```bash
curl -X POST https://localhost:5001/api/pricing/import \
  -H "Content-Type: application/json" \
  -d @sample-pricing.json
```

- [ ] A valid file returns `200 OK` with `imported: true` and a count
- [ ] An invalid file returns `400` with `{"error":"invalid_file",...}`
- [ ] A request from a non-localhost address returns `403 Forbidden`

## Step 5: Verify telemetry in Grafana

- [ ] Open Grafana at `http://localhost:3000`
- [ ] Go to **Explore → Loki**: filter by `service.name=LLMCostControl.Tracker.Api`
      — logs from the check/capture requests are visible
- [ ] Go to **Explore → Tempo**: find the trace for the check/capture request —
      spans carry `effective_group` and `budget_source` tags
- [ ] Go to **Explore → Prometheus**: query `budget_check_total` — the counter
      shows the check requests with `budget_source` and `effective_group` labels

## Step 6: Verify spend accrual in the admin app

- [ ] Open the admin app → **Caller Info** page
- [ ] Enter the caller id used in the capture request
- [ ] The **Effective Budget** section shows the resolved budget (source + amount)
- [ ] The **Running Spend** section shows the accrued spend from the capture
- [ ] The **Recent Usage Events** table shows the capture event with model,
      token counts, cost, and running spend after

## Step 7: Teardown

```bash
docker compose down -v   # wipes volumes for a clean slate
```

---

## Sign-off

- [ ] All items above are checked
- [ ] Contract tests are green in CI (`dotnet test --filter ContractTests`)
- [ ] The `requests.http` file is committed to the repo

**Signed off by:** _______________________ **Date:** ____________
