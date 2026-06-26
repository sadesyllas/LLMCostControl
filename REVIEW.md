# LLM Cost Tracker - Code Review & Architecture Report

This report presents a thorough review of the current LLM Cost Tracker codebase, highlighting critical architectural, security, testing, and operational issues. Each section identifies the problem, specifies where it occurs in the code with clickable file links, and provides recommendations for remediation.

---

## 1. Architectural Issues & Risks

### A. Orleans State-Sync Corruption on Write Failure
* **File Location:** [UserBudgetGrain.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Grains/Implementations/UserBudgetGrain.cs#L137-L140)
* **Problem:** In [UserBudgetGrain.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Grains/Implementations/UserBudgetGrain.cs), the cost of captured usage is directly added to the in-memory `RunningSpendAmount` state before `WriteStateAsync()` is called and awaited. If the database write fails (e.g. temporary database disconnect, lock timeout, connection pool exhaustion), the grain throws an exception, but the mutated state remains in memory. Subsequent check or capture calls will use this corrupted in-memory state, causing the grain's spend calculations to become permanently out-of-sync with the database.
* **Recommendation:** Wrap state writes in a `try-catch` block. If `WriteStateAsync` throws an exception, call `DeactivateOnIdle()` on the grain immediately. This forces Orleans to terminate the activation so that the next request reconstructs the grain from the latest persisted database state rather than serving corrupted in-memory data.

### B. Currency Mismatch & Bypass of Domain Validation
* **File Locations:** 
  * [UserBudgetGrainState.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Grains/State/UserBudgetGrainState.cs#L26-L32)
  * [UserBudgetGrain.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Grains/Implementations/UserBudgetGrain.cs#L137-L140)
* **Problem:** In [UserBudgetGrainState.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Grains/State/UserBudgetGrainState.cs), the running spend is tracked using primitive fields (`decimal RunningSpendAmount` and `string RunningSpendCurrency`) rather than the domain `Money` object. This bypasses the domain's currency-mismatch validation guards. If a caller processes queries under different model currencies (e.g. a model pricing file is loaded in EUR, and another in USD), the grain directly performs `state.RunningSpendAmount += cost` and overrides `state.RunningSpendCurrency = currency`. This results in adding mismatched currencies (e.g. 5.00 USD + 2.00 EUR = 7.00 EUR) without any conversion.
* **Recommendation:** Enforce currency validation when updating running spend. If the incoming cost currency does not match the active running spend currency (and the running spend is non-zero), either block the capture, throw an exception, or implement a currency exchange helper.

### C. Over-Constrained Global Unique Model Index
* **File Location:** [CostTrackerDbContext.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Infrastructure/Data/CostTrackerDbContext.cs#L165)
* **Problem:** In [CostTrackerDbContext.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Infrastructure/Data/CostTrackerDbContext.cs), the table `model_pricing` has a unique index configured solely on the `Model` property (`e.HasIndex(x => x.Model).IsUnique()`). This assumes model names are globally unique across all provider networks. If different providers host overlapping model names (or if a custom provider wraps a model name matching an existing definition), the database will reject the insert due to key violation.
* **Recommendation:** Change the index configuration to a composite unique index covering both `Provider` and `Model`:
  ```csharp
  e.HasIndex(x => new { x.Provider, x.Model }).IsUnique();
  ```

### D. Unused Blazor Scoped DI Lifetime Hazards
* **File Location:** [Program.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Admin.App/Program.cs#L22-L32)
* **Problem:** In [Program.cs (Admin App)](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Admin.App/Program.cs), scoped instances of the DbContext and all repository classes (`GroupRepository`, `GroupBudgetRepository`, etc.) are registered in the DI container. However, in Blazor Interactive Server mode, a client connection is backed by a long-lived "circuit scope". Resolving scoped database contexts or repositories within Blazor components results in a single database connection being held open for the lifetime of the connection. This can lead to memory bloat, entity cache pollution, and concurrency exceptions when asynchronous renders overlap.
* **Note:** Currently, all pages correctly bypass this issue by injecting `IDbContextFactory<CostTrackerDbContext>` and manually instantiating short-lived contexts. However, the registered scoped DI services are dead code and create a trap for future developers.
* **Recommendation:** Remove the unused scoped repository and DbContext registrations from [Program.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Admin.App/Program.cs), or register the repositories as `Transient` to decouple them safely.

---

## 2. Security & Validation Issues

### A. Non-Atomic Pricing Bulk Upload (Partial Imports)
* **File Location:** [Pricing.razor](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Admin.App/Components/Pages/Pricing.razor#L167-L171)
* **Problem:** When an administrator uploads a pricing JSON file in [Pricing.razor](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Admin.App/Components/Pages/Pricing.razor), the entries are grouped by provider, and the code calls `ReplaceProviderPricingAsync` inside a loop:
  ```csharp
  var grouped = parseResult.Entries.GroupBy(p => p.Provider);
  foreach (var group in grouped)
  {
      await repo.ReplaceProviderPricingAsync(group.Key, group.ToList());
  }
  ```
  Since `ReplaceProviderPricingAsync` creates, saves, and commits its own transaction, the upload is NOT atomic across providers. If a file contains model prices for `openai` and `google`, and the insert for `google` fails, the `openai` prices are already committed. This violates specification §12.3: "Mismatches or validation failures reject the import completely. No partial imports."
* **Recommendation:** Wrap the entire upload loop in a single, overarching transaction in the page code, or implement a single bulk replacement method on the repository level that handles all provider replacements within a single unit of work.

### B. Loopback Verification Bypass in Container Networks
* **File Location:** [Program.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Tracker.Api/Program.cs#L338-L356)
* **Problem:** The pricing import endpoint `/api/pricing/import` relies on `IsLocalConnection()` to restrict requests to `localhost`. In containerized environments (such as Docker Compose or Kubernetes bridge networks), host-to-container routing or inter-container routing maps the request source IP to the bridge gateway (e.g. `172.18.0.1`), which is not loopback. This will block legitimate administrators attempting to run local administrative scripts against the port exposed on the host.
* **Recommendation:** Move away from raw IP loopback checks. Instead, secure administrative endpoints (like import) using token-based authorization (OIDC) or a dedicated API key passed in request headers.

### C. HTTP Security Configuration (Insecure JWT Configuration)
* **File Location:** [Program.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Tracker.Api/Program.cs#L53)
* **Problem:** In [Program.cs (Tracker API)](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Tracker.Api/Program.cs), the JWT options have `RequireHttpsMetadata = false` hardcoded. While acceptable in local Development mode, running this setting in production exposes the JWKS endpoint retrieval to man-in-the-middle attacks, potentially allowing attackers to sign counterfeit gateway tokens.
* **Recommendation:** Set `RequireHttpsMetadata = !builder.Environment.IsDevelopment()`.

---

## 3. Testing Code Quality & Flakiness

### A. Shared Mutable Singletons in Orleans Tests
* **File Location:** [GrainTestBase.cs](file:///Users/bellerophon/tmp/LLMCostControl/tests/LLMCostControl.Grains.Tests/GrainTestBase.cs#L20-L70)
* **Problem:** The base class [GrainTestBase.cs](file:///Users/bellerophon/tmp/LLMCostControl/tests/LLMCostControl.Grains.Tests/GrainTestBase.cs) configures static singleton instances for stores (`SharedPricingStore.Instance`, `SharedBudgetStore.Instance`, `SharedUsageEventStore.Instance`). Because Orleans test clusters are expensive to deploy, a single shared `GrainClusterFixture` is used. However, because the test classes share these mutable static stores, any data mutations (e.g., seeding budgets or writing events) in one test class can leak into other test classes running in parallel. The codebase currently mitigates this by requiring developers to use unique caller IDs, which is error-prone.
* **Recommendation:** Implement a clean-up mechanism (e.g., an `IAsyncLifetime` implementation in the test classes or fixture that clears the in-memory dictionaries of the stores before each test run).

### B. Inconsistent SQLite Mappings in Test Harness
* **File Location:** [CostTrackerDbContext.cs](file:///Users/bellerophon/tmp/LLMCostControl/src/LLMCostControl.Infrastructure/Data/CostTrackerDbContext.cs#L55-L70)
* **Problem:** To keep Blazor component tests fast and lightweight, `SqliteTestDbContextFactory` was introduced to run bUnit tests against in-memory SQLite databases rather than full Postgres Testcontainers. However, SQLite lacks native support for `DateTimeOffset` comparisons and sorting, which causes standard LINQ queries like `.OrderBy(x => x.CapturedAt)` to fail under SQLite. While this has been addressed by applying a global `DateTimeOffsetToBinaryConverter` inside the DbContext if `Database.ProviderName` is SQLite, this divergent configuration means tests run on a database schema and translation engine that differs from production.
* **Recommendation:** Continually monitor testing query patterns to ensure SQLite-specific converters do not hide database compatibility issues, or transition performance-critical query tests to Postgres-backed integration runs.
