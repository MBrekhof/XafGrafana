using Xunit;

// Disable test parallelization because multiple test classes share static Prometheus
// metrics (XafMetrics counters/histograms) and use before/after delta assertions.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
