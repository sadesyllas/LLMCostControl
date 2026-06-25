using Xunit;

// Disable parallel test execution: each test class spins up its own
// WebApplicationFactory<Program> host, and several pieces of state are
// process-global — the OpenTelemetry tracer/meter providers, the Serilog
// static logger, and the Orleans localhost cluster. Running classes in
// parallel lets one host's pipeline capture or overwrite another's telemetry
// (M15 asserts on captured spans/metrics/logs), so we serialize the assembly.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true)]
