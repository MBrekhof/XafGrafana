using System;
using System.Reflection;
using FluentAssertions;
using Xunit;
using XafGrafana.Blazor.Server.Services;

namespace XafGrafana.Tests.ServiceTests;

/// <summary>
/// Tests the EfCoreMetricsInterceptor's duration recording via reflection on the private
/// RecordDuration method. The public interceptor overrides (ReaderExecuted, NonQueryExecuted, etc.)
/// cannot be tested through InMemory EF Core because the InMemory provider does not fire
/// DbCommandInterceptor callbacks — it bypasses the ADO.NET command pipeline entirely.
/// Testing through the public API would require a real database (SQLite or SQL Server).
/// </summary>
public class EfCoreMetricsInterceptorTests
{
    private static readonly MethodInfo RecordDurationMethod =
        typeof(EfCoreMetricsInterceptor)
            .GetMethod("RecordDuration", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void RecordDuration_IncrementsHistogramCount()
    {
        var beforeCount = XafMetrics.EfQueryDuration.Count;

        RecordDurationMethod.Invoke(null, new object[] { TimeSpan.FromMilliseconds(42) });

        XafMetrics.EfQueryDuration.Count.Should().Be(beforeCount + 1);
    }

    [Fact]
    public void RecordDuration_ObservesCorrectValue()
    {
        var beforeSum = XafMetrics.EfQueryDuration.Sum;
        var duration = TimeSpan.FromMilliseconds(100);

        RecordDurationMethod.Invoke(null, new object[] { duration });

        var afterSum = XafMetrics.EfQueryDuration.Sum;
        (afterSum - beforeSum).Should().BeApproximately(0.1, 0.001); // 100ms = 0.1s
    }

    [Fact]
    public void RecordDuration_MultipleObservations_AccumulatesCount()
    {
        var beforeCount = XafMetrics.EfQueryDuration.Count;

        RecordDurationMethod.Invoke(null, new object[] { TimeSpan.FromMilliseconds(10) });
        RecordDurationMethod.Invoke(null, new object[] { TimeSpan.FromMilliseconds(20) });
        RecordDurationMethod.Invoke(null, new object[] { TimeSpan.FromMilliseconds(30) });

        XafMetrics.EfQueryDuration.Count.Should().Be(beforeCount + 3);
    }
}
