

// Disable parallel test execution: Orleans TestClusters from different test
// classes can interfere when running simultaneously.
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true)]
