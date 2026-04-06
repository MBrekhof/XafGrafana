using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using XafGrafana.Module.BusinessObjects;

namespace XafGrafana.Blazor.Server.Services;

public class ChaosTestService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChaosTestService> _logger;
    private readonly Random _random = new();
    private readonly List<byte[]> _memoryPressureList = new();

    public ChaosTestService(IServiceProvider serviceProvider, ILogger<ChaosTestService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!StressTestManager.IsRunning(StressTestType.ChaosTest))
            {
                ReleaseMemoryPressure();
                await Task.Delay(500, stoppingToken);
                continue;
            }

            try
            {
                var preset = StressTestManager.ActivePreset;
                RunChaosScenarios(preset);
                StressTestManager.IncrementOps();

                var delayMs = preset switch
                {
                    StressTestPreset.Light => 5000,
                    StressTestPreset.Medium => 2000,
                    StressTestPreset.Heavy => 1000,
                    _ => 5000
                };

                await Task.Delay(delayMs, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Chaos test cycle failed");
                await Task.Delay(500, stoppingToken);
            }
        }
    }

    private void RunChaosScenarios(StressTestPreset preset)
    {
        // Light: slow queries only
        // Medium: slow queries + error bursts
        // Heavy: all three
        ExecuteSlowQuery();

        if (preset >= StressTestPreset.Medium)
            ExecuteErrorBurst();

        if (preset >= StressTestPreset.Heavy)
            ExecuteMemoryPressure();
    }

    private void ExecuteSlowQuery()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
            using var os = factory.CreateNonSecuredObjectSpace<Order>();

            // Deliberately unoptimized: load ALL orders, enumerate multiple times,
            // access navigation properties to trigger N+1 queries
            var allOrders = os.GetObjects<Order>().ToList();

            // Force N+1 by accessing Customer.Name for every order
            var summary = allOrders
                .GroupBy(o => o.Customer?.Name ?? "Unknown")
                .Select(g => new { Customer = g.Key, Total = g.Sum(o => o.Amount), Count = g.Count() })
                .OrderByDescending(x => x.Total)
                .ToList();

            // Enumerate again — deliberate waste
            var statusCounts = allOrders
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            // One more pass with sorting
            var recentOrders = allOrders
                .OrderByDescending(o => o.OrderDate)
                .Take(100)
                .ToList();

            _logger.LogDebug("Slow query: {OrderCount} orders, {CustomerCount} customers",
                allOrders.Count, summary.Count);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Slow query chaos (expected)");
            XafMetrics.ErrorsTotal.WithLabels("chaos_slow_query").Inc();
        }
    }

    private void ExecuteErrorBurst()
    {
        // Generate 3-5 errors per burst
        var errorCount = _random.Next(3, 6);

        for (int i = 0; i < errorCount; i++)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
                using var os = factory.CreateNonSecuredObjectSpace<Customer>();

                var errorType = _random.Next(3);

                switch (errorType)
                {
                    case 0:
                        // Create customer with deliberately null name, then try to find by non-existent key
                        var customer = os.CreateObject<Customer>();
                        customer.Name = null!;
                        customer.Email = null!;
                        customer.City = null!;
                        os.CommitChanges();
                        // This succeeds but produces dirty data — count it as an error scenario
                        XafMetrics.ErrorsTotal.WithLabels("chaos_null_data").Inc();
                        // Clean up immediately
                        os.Delete(customer);
                        os.CommitChanges();
                        break;

                    case 1:
                        // Try to get a non-existent object
                        var fakeId = Guid.NewGuid();
                        var missing = os.GetObjectByKey<Customer>(fakeId);
                        if (missing == null)
                        {
                            XafMetrics.ErrorsTotal.WithLabels("chaos_not_found").Inc();
                        }
                        break;

                    case 2:
                        // Create and immediately delete in rapid succession
                        var tempCustomer = os.CreateObject<Customer>();
                        tempCustomer.Name = "CHAOS_TEMP";
                        tempCustomer.Email = "chaos@test.com";
                        tempCustomer.City = "Nowhere";
                        os.CommitChanges();
                        os.Delete(tempCustomer);
                        os.CommitChanges();
                        XafMetrics.ErrorsTotal.WithLabels("chaos_rapid_lifecycle").Inc();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error burst chaos (expected)");
                XafMetrics.ErrorsTotal.WithLabels("chaos_exception").Inc();
            }
        }
    }

    private void ExecuteMemoryPressure()
    {
        try
        {
            // Allocate 10-50 MB in chunks
            var chunkCount = _random.Next(1, 6);
            for (int i = 0; i < chunkCount; i++)
            {
                var size = _random.Next(2, 10) * 1024 * 1024; // 2-10 MB chunks
                var buffer = new byte[size];
                // Touch the memory to ensure it's actually allocated
                _random.NextBytes(buffer.AsSpan(0, Math.Min(4096, size)));
                _memoryPressureList.Add(buffer);
            }

            _logger.LogDebug("Memory pressure: holding {Count} chunks, ~{SizeMB} MB",
                _memoryPressureList.Count,
                _memoryPressureList.Sum(b => b.Length) / (1024 * 1024));

            // Keep max 20 chunks (~200 MB), release oldest when over
            while (_memoryPressureList.Count > 20)
            {
                _memoryPressureList.RemoveAt(0);
            }

            // Occasionally release everything to create GC pressure pattern
            if (_random.Next(5) == 0)
            {
                ReleaseMemoryPressure();
                GC.Collect(2, GCCollectionMode.Forced);
                _logger.LogDebug("Memory pressure: forced GC collection");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Memory pressure chaos (expected)");
            ReleaseMemoryPressure();
        }
    }

    private void ReleaseMemoryPressure()
    {
        if (_memoryPressureList.Count > 0)
        {
            _memoryPressureList.Clear();
            _logger.LogDebug("Memory pressure: released all chunks");
        }
    }
}
