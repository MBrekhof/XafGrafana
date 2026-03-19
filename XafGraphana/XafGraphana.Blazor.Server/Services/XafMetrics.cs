using Prometheus;

namespace XafGraphana.Blazor.Server.Services;

public static class XafMetrics
{
    public static readonly Counter ObjectCrudTotal = Metrics.CreateCounter(
        "xaf_object_crud_total",
        "Total XAF object CRUD operations",
        new CounterConfiguration { LabelNames = new[] { "entity", "operation" } });

    public static readonly Counter LoginTotal = Metrics.CreateCounter(
        "xaf_login_total",
        "Total login attempts",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    public static readonly Gauge ActiveSessions = Metrics.CreateGauge(
        "xaf_active_sessions",
        "Number of active Blazor circuits");

    public static readonly Counter ErrorsTotal = Metrics.CreateCounter(
        "xaf_errors_total",
        "Total application errors",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    public static readonly Histogram EfQueryDuration = Metrics.CreateHistogram(
        "ef_query_duration_seconds",
        "EF Core query execution duration in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 15)
        });
}
