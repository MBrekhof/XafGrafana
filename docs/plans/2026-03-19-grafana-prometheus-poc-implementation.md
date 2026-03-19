# XAF Blazor + Grafana/Prometheus POC — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add full-stack observability (app metrics, SQL Server metrics, Grafana dashboards) to a blank XAF Blazor Server application, with simulated load, to evaluate value for GAT acceptance testing.

**Architecture:** XAF app runs locally via `dotnet run`, exposes `/metrics` via prometheus-net. Docker Compose runs SQL Server, sql_exporter, Prometheus, and Grafana. Prometheus scrapes the app and sql_exporter. Grafana auto-provisions 4 dashboards.

**Tech Stack:** .NET 8, DevExpress XAF 25.2.3, EF Core, prometheus-net, Docker Compose, Prometheus, Grafana, sql_exporter

---

### Task 1: Docker Compose — SQL Server

**Files:**
- Create: `docker-compose.yml` (project root: `C:\Projects\XafGraphana`)

**Step 1: Create docker-compose.yml with SQL Server service**

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: xafgrafana-sqlserver
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "YourStr0ng!Passw0rd"
      MSSQL_PID: "Developer"
    ports:
      - "1433:1433"
    volumes:
      - sqlserver-data:/var/opt/mssql
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "YourStr0ng!Passw0rd" -C -Q "SELECT 1" || exit 1
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  sqlserver-data:
```

**Step 2: Start SQL Server and verify it's healthy**

Run: `docker compose up -d sqlserver`
Then: `docker compose ps`
Expected: sqlserver shows "healthy"

**Step 3: Update connection string in appsettings.Development.json**

Modify: `XafGraphana/XafGraphana.Blazor.Server/appsettings.Development.json`

Add ConnectionStrings section that overrides the LocalDB default:

```json
{
  "ConnectionStrings": {
    "ConnectionString": "Server=localhost,1433;Database=XafGraphana;User Id=sa;Password=YourStr0ng!Passw0rd;TrustServerCertificate=True;MultipleActiveResultSets=True"
  },
  "DetailedErrors": true,
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "DevExpress.ExpressApp": "Information"
    }
  }
}
```

**Step 4: Verify XAF app starts against Docker SQL Server**

Run: `cd XafGraphana/XafGraphana.Blazor.Server && dotnet run`
Expected: App starts, creates database, no connection errors in output.
Stop the app after verifying.

**Step 5: Commit**

```bash
git add docker-compose.yml XafGraphana/XafGraphana.Blazor.Server/appsettings.Development.json
git commit -m "feat: add Docker SQL Server and update dev connection string"
```

---

### Task 2: Business Objects — Customer and Order

**Files:**
- Create: `XafGraphana/XafGraphana.Module/BusinessObjects/Customer.cs`
- Create: `XafGraphana/XafGraphana.Module/BusinessObjects/Order.cs`
- Modify: `XafGraphana/XafGraphana.Module/BusinessObjects/XafGraphanaDbContext.cs:17-25`
- Modify: `XafGraphana/XafGraphana.Module/Module.cs:23-38`

**Step 1: Create the OrderStatus enum and Order entity**

Create `XafGraphana/XafGraphana.Module/BusinessObjects/Order.cs`:

```csharp
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafGraphana.Module.BusinessObjects;

public enum OrderStatus
{
    New,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}

[DefaultClassOptions]
[NavigationItem("Business")]
public class Order : BaseObject
{
    public virtual DateTime OrderDate { get; set; }

    public virtual decimal Amount { get; set; }

    public virtual OrderStatus Status { get; set; }

    public virtual Customer Customer { get; set; }
}
```

**Step 2: Create the Customer entity**

Create `XafGraphana/XafGraphana.Module/BusinessObjects/Customer.cs`:

```csharp
using System.ComponentModel;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafGraphana.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
[NavigationItem("Business")]
public class Customer : BaseObject
{
    public virtual string Name { get; set; }

    public virtual string Email { get; set; }

    public virtual string City { get; set; }

    public virtual IList<Order> Orders { get; set; } = new List<Order>();
}
```

**Step 3: Register DbSets in XafGraphanaEFCoreDbContext**

Modify: `XafGraphana/XafGraphana.Module/BusinessObjects/XafGraphanaDbContext.cs`

Add after the `HCategories` DbSet (line 25):

```csharp
public DbSet<Customer> Customers { get; set; }
public DbSet<Order> Orders { get; set; }
```

**Step 4: Export new types in Module.cs**

Modify: `XafGraphana/XafGraphana.Module/Module.cs`

Add inside the constructor, after the existing `AdditionalExportedTypes` lines (after line 38):

```csharp
AdditionalExportedTypes.Add(typeof(XafGraphana.Module.BusinessObjects.Customer));
AdditionalExportedTypes.Add(typeof(XafGraphana.Module.BusinessObjects.Order));
```

**Step 5: Verify the app starts and shows new entities**

Run: `cd XafGraphana/XafGraphana.Blazor.Server && dotnet run`
Expected: App starts. Log in as Admin (empty password). Customer and Order nav items visible under "Business" group. Stop the app.

**Step 6: Commit**

```bash
git add XafGraphana/XafGraphana.Module/BusinessObjects/Customer.cs XafGraphana/XafGraphana.Module/BusinessObjects/Order.cs XafGraphana/XafGraphana.Module/BusinessObjects/XafGraphanaDbContext.cs XafGraphana/XafGraphana.Module/Module.cs
git commit -m "feat: add Customer and Order business objects"
```

---

### Task 3: Prometheus Metrics — NuGet, Static Metrics, Middleware

**Files:**
- Modify: `XafGraphana/XafGraphana.Blazor.Server/XafGraphana.Blazor.Server.csproj:19-33`
- Create: `XafGraphana/XafGraphana.Blazor.Server/Services/XafMetrics.cs`
- Modify: `XafGraphana/XafGraphana.Blazor.Server/Startup.cs:35-207` (ConfigureServices)
- Modify: `XafGraphana/XafGraphana.Blazor.Server/Startup.cs:210-242` (Configure)

**Step 1: Add prometheus-net NuGet packages**

Run:
```bash
cd XafGraphana/XafGraphana.Blazor.Server
dotnet add package prometheus-net.AspNetCore
```

(This pulls in `prometheus-net` as a transitive dependency.)

**Step 2: Create XafMetrics.cs — custom metric definitions**

Create `XafGraphana/XafGraphana.Blazor.Server/Services/XafMetrics.cs`:

```csharp
using Prometheus;

namespace XafGraphana.Blazor.Server.Services;

public static class XafMetrics
{
    // CRUD operations counter — labels: entity, operation
    public static readonly Counter ObjectCrudTotal = Metrics.CreateCounter(
        "xaf_object_crud_total",
        "Total XAF object CRUD operations",
        new CounterConfiguration { LabelNames = new[] { "entity", "operation" } });

    // Login counter — labels: result (success/failure)
    public static readonly Counter LoginTotal = Metrics.CreateCounter(
        "xaf_login_total",
        "Total login attempts",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    // Active Blazor circuits gauge
    public static readonly Gauge ActiveSessions = Metrics.CreateGauge(
        "xaf_active_sessions",
        "Number of active Blazor circuits");

    // Error counter — labels: type
    public static readonly Counter ErrorsTotal = Metrics.CreateCounter(
        "xaf_errors_total",
        "Total application errors",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    // EF Core query duration histogram
    public static readonly Histogram EfQueryDuration = Metrics.CreateHistogram(
        "ef_query_duration_seconds",
        "EF Core query execution duration in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 15) // 1ms to ~16s
        });
}
```

**Step 3: Register Prometheus middleware in Startup.cs**

Modify `Startup.cs` — add to the top of `ConfigureServices` (after line 36, before `services.AddRazorPages()`):

```csharp
services.AddHealthChecks();
```

Modify `Startup.cs` — in the `Configure` method, add Prometheus HTTP metrics middleware. Insert after `app.UseRouting();` (line 230) and before `app.UseAuthentication();` (line 231):

```csharp
app.UseHttpMetrics(); // prometheus-net: tracks HTTP request duration/count
```

Modify `Startup.cs` — inside the `app.UseEndpoints(endpoints => { ... })` block, add before `endpoints.MapControllers();` (line 240):

```csharp
endpoints.MapMetrics(); // exposes /metrics for Prometheus
endpoints.MapHealthChecks("/health");
```

Add the using at the top of Startup.cs:

```csharp
using Prometheus;
```

**Step 4: Verify /metrics endpoint works**

Run: `cd XafGraphana/XafGraphana.Blazor.Server && dotnet run`
Open: `http://localhost:5000/metrics`
Expected: Prometheus text format output with `http_request_duration_seconds`, `dotnet_*`, `process_*` metrics.
Stop the app.

**Step 5: Commit**

```bash
git add XafGraphana/XafGraphana.Blazor.Server/XafGraphana.Blazor.Server.csproj XafGraphana/XafGraphana.Blazor.Server/Services/XafMetrics.cs XafGraphana/XafGraphana.Blazor.Server/Startup.cs
git commit -m "feat: add prometheus-net metrics endpoint and custom metric definitions"
```

---

### Task 4: EF Core Metrics Interceptor

**Files:**
- Create: `XafGraphana/XafGraphana.Blazor.Server/Services/EfCoreMetricsInterceptor.cs`
- Modify: `XafGraphana/XafGraphana.Blazor.Server/Startup.cs:78-96` (DbContext options)

**Step 1: Create EfCoreMetricsInterceptor**

Create `XafGraphana/XafGraphana.Blazor.Server/Services/EfCoreMetricsInterceptor.cs`:

```csharp
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace XafGraphana.Blazor.Server.Services;

public class EfCoreMetricsInterceptor : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        RecordDuration(eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        RecordDuration(eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        RecordDuration(eventData.Duration);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        RecordDuration(eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        RecordDuration(eventData.Duration);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        RecordDuration(eventData.Duration);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private static void RecordDuration(TimeSpan duration)
    {
        XafMetrics.EfQueryDuration.Observe(duration.TotalSeconds);
    }
}
```

**Step 2: Register the interceptor in the DbContext options**

Modify `Startup.cs` — inside the `.WithDbContext<XafGraphanaEFCoreDbContext>` lambda (around line 78-96), add the interceptor registration. After `options.UseConnectionString(connectionString);` (line 95) and before the closing `})` (line 96):

```csharp
options.UseInterceptor(new EfCoreMetricsInterceptor());
```

Note: XAF's `UseConnectionString` sets up SQL Server. The `UseInterceptor` extension is from `DevExpress.ExpressApp.EFCore`. If XAF doesn't expose `UseInterceptor` on its options builder, an alternative approach is to register the interceptor as a singleton service:

In `ConfigureServices`, add:
```csharp
services.AddSingleton<EfCoreMetricsInterceptor>();
```

And in the DbContext options lambda, access it via the serviceProvider:
```csharp
var interceptor = serviceProvider.GetRequiredService<EfCoreMetricsInterceptor>();
```

Then configure the underlying EF Core DbContextOptionsBuilder to add it. The exact approach depends on what XAF's options builder exposes — check at implementation time.

**Step 3: Verify EF Core metrics appear**

Run the app, perform a few operations (login, navigate), check `/metrics`.
Expected: `ef_query_duration_seconds_bucket`, `ef_query_duration_seconds_sum`, `ef_query_duration_seconds_count` present.

**Step 4: Commit**

```bash
git add XafGraphana/XafGraphana.Blazor.Server/Services/EfCoreMetricsInterceptor.cs XafGraphana/XafGraphana.Blazor.Server/Startup.cs
git commit -m "feat: add EF Core query duration metrics interceptor"
```

---

### Task 5: Circuit Handler — Active Sessions Metric

**Files:**
- Modify: `XafGraphana/XafGraphana.Blazor.Server/Services/CircuitHandlerProxy.cs:13-24`

**Step 1: Add session tracking to CircuitHandlerProxy**

Modify `CircuitHandlerProxy.cs` — update `OnCircuitOpenedAsync` and `OnCircuitClosedAsync` to increment/decrement the gauge:

In `OnCircuitOpenedAsync` (line 13-16), add before the return:
```csharp
XafMetrics.ActiveSessions.Inc();
```

In `OnCircuitClosedAsync` (line 21-24), add before the return:
```csharp
XafMetrics.ActiveSessions.Dec();
```

Add using at top:
```csharp
using XafGraphana.Blazor.Server.Services;
```

Wait — `CircuitHandlerProxy` is already in the `Services` namespace, so just ensure `XafMetrics` is accessible (it's in the same namespace). No extra using needed.

**Step 2: Verify active sessions metric**

Run app, open browser, check `/metrics`.
Expected: `xaf_active_sessions 1` (or more if multiple tabs).

**Step 3: Commit**

```bash
git add XafGraphana/XafGraphana.Blazor.Server/Services/CircuitHandlerProxy.cs
git commit -m "feat: track active Blazor circuits in Prometheus gauge"
```

---

### Task 6: CRUD Metrics Controller

**Files:**
- Create: `XafGraphana/XafGraphana.Blazor.Server/Controllers/MetricsViewController.cs`

Note: This controller lives in the Blazor.Server project (not Module) because it references `XafMetrics` which depends on prometheus-net. The Module project should stay free of prometheus-net dependencies.

**Step 1: Create MetricsViewController**

Create `XafGraphana/XafGraphana.Blazor.Server/Controllers/MetricsViewController.cs`:

```csharp
using DevExpress.ExpressApp;
using XafGraphana.Blazor.Server.Services;

namespace XafGraphana.Blazor.Server.Controllers;

public class MetricsViewController : ViewController
{
    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.Committed += ObjectSpace_Committed;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.Committed += ObjectSpace_Committed;
        base.OnDeactivated();
    }

    private void ObjectSpace_Committed(object sender, EventArgs e)
    {
        if (sender is not IObjectSpace objectSpace) return;

        var modifications = objectSpace.ModifiedObjects;
        // Note: After commit, ModifiedObjects may be empty.
        // We use ObjectSpace events differently — see alternative below.
    }
}
```

Actually, a better approach for XAF CRUD tracking is to hook into `IObjectSpace.ObjectChanged` and `IObjectSpace.Committing` events, or use `ObjectSpace.ModifiedObjects` before commit. But the simplest reliable approach for a POC is to hook `IObjectSpace.Committing`:

```csharp
using DevExpress.ExpressApp;
using XafGraphana.Blazor.Server.Services;

namespace XafGraphana.Blazor.Server.Controllers;

public class MetricsViewController : ViewController
{
    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.Committing += ObjectSpace_Committing;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.Committing -= ObjectSpace_Committing;
        base.OnDeactivated();
    }

    private void ObjectSpace_Committing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not IObjectSpace objectSpace) return;

        foreach (var obj in objectSpace.ModifiedObjects)
        {
            if (obj == null) continue;
            var entityName = obj.GetType().Name;
            var state = objectSpace.GetObjectState(obj);

            switch (state)
            {
                case ObjectState.New:
                    XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Create").Inc();
                    break;
                case ObjectState.Modified:
                    XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Update").Inc();
                    break;
                case ObjectState.Deleted:
                    XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Delete").Inc();
                    break;
            }
        }
    }
}
```

**Step 2: Verify CRUD metrics appear**

Run app, log in as Admin, create a Customer, check `/metrics`.
Expected: `xaf_object_crud_total{entity="Customer",operation="Create"} 1`

**Step 3: Commit**

```bash
git add XafGraphana/XafGraphana.Blazor.Server/Controllers/MetricsViewController.cs
git commit -m "feat: add XAF CRUD metrics via ViewController"
```

---

### Task 7: Activity Simulator BackgroundService

**Files:**
- Create: `XafGraphana/XafGraphana.Blazor.Server/Services/ActivitySimulatorService.cs`
- Modify: `XafGraphana/XafGraphana.Blazor.Server/Startup.cs:35-36` (register hosted service)

**Step 1: Create ActivitySimulatorService**

Create `XafGraphana/XafGraphana.Blazor.Server/Services/ActivitySimulatorService.cs`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using XafGraphana.Module.BusinessObjects;

namespace XafGraphana.Blazor.Server.Services;

public class ActivitySimulatorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ActivitySimulatorService> _logger;
    private readonly Random _random = new();

    private static readonly string[] Cities =
        { "Amsterdam", "Rotterdam", "Utrecht", "Den Haag", "Eindhoven",
          "Groningen", "Tilburg", "Almere", "Breda", "Nijmegen" };

    private static readonly string[] FirstNames =
        { "Jan", "Piet", "Klaas", "Marie", "Anna",
          "Sofie", "Daan", "Liam", "Emma", "Lucas" };

    private static readonly string[] LastNames =
        { "de Vries", "Jansen", "van den Berg", "Bakker", "Visser",
          "Smit", "Meijer", "de Boer", "Mulder", "de Groot" };

    public ActivitySimulatorService(
        IServiceProvider serviceProvider,
        ILogger<ActivitySimulatorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the app to fully start and create the database
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        _logger.LogInformation("Activity simulator started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var action = _random.Next(100);

                if (action < 40)
                    await CreateCustomerAsync(stoppingToken);
                else if (action < 70)
                    await CreateOrderAsync(stoppingToken);
                else if (action < 90)
                    await UpdateOrderStatusAsync(stoppingToken);
                else
                    await DeleteCancelledOrderAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Simulator action failed");
                XafMetrics.ErrorsTotal.WithLabels("simulator").Inc();
            }

            // Random delay 3-12 seconds (busier than design spec for POC demo)
            var delay = TimeSpan.FromSeconds(3 + _random.Next(10));
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task CreateCustomerAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var securityProvider = scope.ServiceProvider
            .GetRequiredService<ISecurityStrategyBase>();

        var objectSpaceFactory = scope.ServiceProvider
            .GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Customer>();

        var first = FirstNames[_random.Next(FirstNames.Length)];
        var last = LastNames[_random.Next(LastNames.Length)];

        var customer = objectSpace.CreateObject<Customer>();
        customer.Name = $"{first} {last}";
        customer.Email = $"{first.ToLower()}.{last.ToLower().Replace(" ", "")}@example.com";
        customer.City = Cities[_random.Next(Cities.Length)];

        objectSpace.CommitChanges();
        XafMetrics.ObjectCrudTotal.WithLabels("Customer", "Create").Inc();
        _logger.LogDebug("Created customer: {Name}", customer.Name);
    }

    private async Task CreateOrderAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var objectSpaceFactory = scope.ServiceProvider
            .GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Order>();

        var customers = objectSpace.GetObjects<Customer>();
        if (!customers.Any()) return;

        var customer = customers.ElementAt(_random.Next(customers.Count));

        var order = objectSpace.CreateObject<Order>();
        order.Customer = customer;
        order.OrderDate = DateTime.Now;
        order.Amount = Math.Round((decimal)(_random.NextDouble() * 500 + 10), 2);
        order.Status = OrderStatus.New;

        objectSpace.CommitChanges();
        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Create").Inc();
        _logger.LogDebug("Created order for {Customer}, amount: {Amount}", customer.Name, order.Amount);
    }

    private async Task UpdateOrderStatusAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var objectSpaceFactory = scope.ServiceProvider
            .GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Order>();

        var orders = objectSpace.GetObjects<Order>()
            .Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)
            .ToList();

        if (!orders.Any()) return;

        var order = orders[_random.Next(orders.Count)];

        // Progress to next status, or occasionally cancel
        if (_random.Next(10) < 2)
            order.Status = OrderStatus.Cancelled;
        else
            order.Status = order.Status + 1; // Next enum value

        objectSpace.CommitChanges();
        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Update").Inc();
        _logger.LogDebug("Updated order status to {Status}", order.Status);
    }

    private async Task DeleteCancelledOrderAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var objectSpaceFactory = scope.ServiceProvider
            .GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Order>();

        var cancelledOrders = objectSpace.GetObjects<Order>()
            .Where(o => o.Status == OrderStatus.Cancelled)
            .ToList();

        if (!cancelledOrders.Any()) return;

        var order = cancelledOrders[_random.Next(cancelledOrders.Count)];
        objectSpace.Delete(order);
        objectSpace.CommitChanges();
        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Delete").Inc();
        _logger.LogDebug("Deleted cancelled order");
    }
}
```

**Step 2: Register the hosted service in Startup.cs**

Modify `Startup.cs` — add at the top of `ConfigureServices` (after `services.AddHealthChecks();` from Task 3):

```csharp
services.AddHostedService<ActivitySimulatorService>();
```

**Step 3: Verify simulator runs**

Run the app. Watch the console output for "Activity simulator started" and subsequent debug log messages.
Check `/metrics` — `xaf_object_crud_total` counters should be incrementing.

**Step 4: Commit**

```bash
git add XafGraphana/XafGraphana.Blazor.Server/Services/ActivitySimulatorService.cs XafGraphana/XafGraphana.Blazor.Server/Startup.cs
git commit -m "feat: add activity simulator background service"
```

---

### Task 8: Docker Compose — Prometheus

**Files:**
- Modify: `docker-compose.yml`
- Create: `monitoring/prometheus/prometheus.yml`

**Step 1: Create Prometheus config**

Create `monitoring/prometheus/prometheus.yml`:

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: "xaf-app"
    metrics_path: /metrics
    static_configs:
      - targets: ["host.docker.internal:5000"]
        labels:
          app: "xafgrafana"

  - job_name: "sql-exporter"
    static_configs:
      - targets: ["sql-exporter:9399"]
        labels:
          app: "sqlserver"
```

**Step 2: Add Prometheus service to docker-compose.yml**

Add to `docker-compose.yml` services section:

```yaml
  prometheus:
    image: prom/prometheus:latest
    container_name: xafgrafana-prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./monitoring/prometheus/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus-data:/prometheus
    extra_hosts:
      - "host.docker.internal:host-gateway"
    depends_on:
      sqlserver:
        condition: service_healthy
```

Add `prometheus-data:` to the volumes section.

**Step 3: Start Prometheus and verify targets**

Run: `docker compose up -d prometheus`
Open: `http://localhost:9090/targets`
Expected: `xaf-app` target shows as UP (if XAF app is running) or DOWN (if not). `sql-exporter` will show as DOWN until we add it.

**Step 4: Commit**

```bash
git add docker-compose.yml monitoring/prometheus/prometheus.yml
git commit -m "feat: add Prometheus to Docker Compose with scrape config"
```

---

### Task 9: Docker Compose — SQL Exporter

**Files:**
- Modify: `docker-compose.yml`
- Create: `monitoring/sql-exporter/sql_exporter.yml`

**Step 1: Create sql_exporter config**

Create `monitoring/sql-exporter/sql_exporter.yml`:

```yaml
global:
  scrape_timeout_offset: 500ms
  min_interval: 0s
  max_connections: 3
  max_idle_connections: 3

target:
  data_source_name: "sqlserver://sa:YourStr0ng!Passw0rd@sqlserver:1433?encrypt=disable"

collectors:
  - collector_name: mssql_standard
    metrics:
      - metric_name: mssql_connections
        type: gauge
        help: "Number of active connections"
        values: [count]
        query: |
          SELECT COUNT(*) AS count
          FROM sys.dm_exec_connections

      - metric_name: mssql_sessions_active
        type: gauge
        help: "Number of active sessions"
        values: [count]
        query: |
          SELECT COUNT(*) AS count
          FROM sys.dm_exec_sessions
          WHERE status = 'running'

      - metric_name: mssql_batch_requests_total
        type: counter
        help: "Total batch requests"
        values: [count]
        query: |
          SELECT cntr_value AS count
          FROM sys.dm_os_performance_counters
          WHERE counter_name = 'Batch Requests/sec'

      - metric_name: mssql_buffer_cache_hit_ratio
        type: gauge
        help: "Buffer cache hit ratio"
        values: [ratio]
        query: |
          SELECT CAST(
            (SELECT cntr_value FROM sys.dm_os_performance_counters
             WHERE counter_name = 'Buffer cache hit ratio') AS FLOAT)
          /
          NULLIF(CAST(
            (SELECT cntr_value FROM sys.dm_os_performance_counters
             WHERE counter_name = 'Buffer cache hit ratio base') AS FLOAT), 0)
          AS ratio

      - metric_name: mssql_deadlocks_total
        type: counter
        help: "Total deadlocks"
        values: [count]
        query: |
          SELECT cntr_value AS count
          FROM sys.dm_os_performance_counters
          WHERE counter_name = 'Number of Deadlocks/sec'
          AND instance_name = '_Total'

      - metric_name: mssql_lock_wait_time_ms
        type: gauge
        help: "Lock wait time in milliseconds"
        values: [wait_time]
        query: |
          SELECT SUM(wait_time_ms) AS wait_time
          FROM sys.dm_os_wait_stats
          WHERE wait_type LIKE 'LCK_%'

      - metric_name: mssql_io_wait_time_ms
        type: gauge
        help: "IO wait time in milliseconds"
        values: [wait_time]
        query: |
          SELECT SUM(wait_time_ms) AS wait_time
          FROM sys.dm_os_wait_stats
          WHERE wait_type LIKE 'PAGEIO%' OR wait_type LIKE 'WRITE%'

      - metric_name: mssql_database_size_bytes
        type: gauge
        help: "Database size in bytes"
        key_labels: [database_name]
        values: [size_bytes]
        query: |
          SELECT
            DB_NAME(database_id) AS database_name,
            SUM(size) * 8 * 1024 AS size_bytes
          FROM sys.master_files
          GROUP BY database_id
```

**Step 2: Add sql-exporter service to docker-compose.yml**

Add to services section:

```yaml
  sql-exporter:
    image: burningalchemist/sql_exporter:latest
    container_name: xafgrafana-sql-exporter
    ports:
      - "9399:9399"
    volumes:
      - ./monitoring/sql-exporter/sql_exporter.yml:/etc/sql_exporter/sql_exporter.yml:ro
    depends_on:
      sqlserver:
        condition: service_healthy
```

**Step 3: Start sql-exporter and verify**

Run: `docker compose up -d sql-exporter`
Check: `http://localhost:9399/metrics`
Expected: `mssql_connections`, `mssql_buffer_cache_hit_ratio`, etc. present.

Check Prometheus targets: `http://localhost:9090/targets`
Expected: Both `xaf-app` and `sql-exporter` targets UP.

**Step 4: Commit**

```bash
git add docker-compose.yml monitoring/sql-exporter/sql_exporter.yml
git commit -m "feat: add SQL Server exporter to Docker Compose"
```

---

### Task 10: Docker Compose — Grafana with Auto-Provisioning

**Files:**
- Modify: `docker-compose.yml`
- Create: `monitoring/grafana/provisioning/datasources/prometheus.yml`
- Create: `monitoring/grafana/provisioning/dashboards/dashboards.yml`

**Step 1: Create Grafana datasource provisioning**

Create `monitoring/grafana/provisioning/datasources/prometheus.yml`:

```yaml
apiVersion: 1

datasources:
  - name: Prometheus
    type: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true
    editable: true
```

**Step 2: Create Grafana dashboard provisioning config**

Create `monitoring/grafana/provisioning/dashboards/dashboards.yml`:

```yaml
apiVersion: 1

providers:
  - name: "XafGraphana"
    orgId: 1
    folder: "XAF Monitoring"
    type: file
    disableDeletion: false
    editable: true
    options:
      path: /var/lib/grafana/dashboards
      foldersFromFilesStructure: false
```

**Step 3: Add Grafana service to docker-compose.yml**

Add to services section:

```yaml
  grafana:
    image: grafana/grafana:latest
    container_name: xafgrafana-grafana
    ports:
      - "3000:3000"
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: admin
      GF_USERS_ALLOW_SIGN_UP: "false"
    volumes:
      - ./monitoring/grafana/provisioning:/etc/grafana/provisioning:ro
      - ./monitoring/grafana/dashboards:/var/lib/grafana/dashboards:ro
      - grafana-data:/var/lib/grafana
    depends_on:
      - prometheus
```

Add `grafana-data:` to the volumes section.

**Step 4: Start Grafana and verify**

Run: `docker compose up -d grafana`
Open: `http://localhost:3000` — login with admin/admin.
Go to: Configuration → Data Sources.
Expected: Prometheus datasource auto-configured and working (green "Data source is working" when tested).

**Step 5: Commit**

```bash
git add docker-compose.yml monitoring/grafana/provisioning/
git commit -m "feat: add Grafana to Docker Compose with auto-provisioned Prometheus datasource"
```

---

### Task 11: Grafana Dashboard — GAT Overview

**Files:**
- Create: `monitoring/grafana/dashboards/gat-overview.json`

**Step 1: Create the GAT Overview dashboard JSON**

This dashboard shows: p95 response time, error rate, uptime, concurrent users.

Create `monitoring/grafana/dashboards/gat-overview.json` with a Grafana dashboard JSON containing these panels:

1. **Stat panel — P95 Response Time**: query `histogram_quantile(0.95, rate(http_request_duration_seconds_bucket[5m]))`
2. **Stat panel — Error Rate**: query `sum(rate(xaf_errors_total[5m]))`
3. **Stat panel — Active Sessions**: query `xaf_active_sessions`
4. **Stat panel — Uptime**: query `process_start_time_seconds` formatted as time-since
5. **Time series — Response Time Percentiles**: p50, p95, p99 over time
6. **Time series — Memory Usage**: query `process_working_set_bytes` and `dotnet_gc_heap_size_bytes`
7. **Time series — Error Rate Over Time**: query `sum(rate(xaf_errors_total[5m])) by (type)`
8. **Time series — GC Collections**: query `rate(dotnet_collection_count_total[5m])` by generation

The JSON will be a complete Grafana dashboard export. Generate the full JSON at implementation time using Grafana's dashboard model.

**Step 2: Verify dashboard loads in Grafana**

Restart Grafana: `docker compose restart grafana`
Open: `http://localhost:3000` → Dashboards → XAF Monitoring → GAT Overview
Expected: Panels render with data (if app is running) or show "No data" (if not).

**Step 3: Commit**

```bash
git add monitoring/grafana/dashboards/gat-overview.json
git commit -m "feat: add GAT Overview Grafana dashboard"
```

---

### Task 12: Grafana Dashboard — Application Performance

**Files:**
- Create: `monitoring/grafana/dashboards/app-performance.json`

**Step 1: Create the Application Performance dashboard JSON**

Panels:
1. **Heatmap — HTTP Request Duration**: query `rate(http_request_duration_seconds_bucket[5m])`
2. **Time series — Requests/sec**: query `rate(http_request_duration_seconds_count[5m])`
3. **Time series — CPU Usage**: query `rate(process_cpu_seconds_total[5m])`
4. **Time series — Memory (Working Set)**: query `process_working_set_bytes`
5. **Time series — Thread Pool**: queries for `dotnet_threadpool_queue_length`, `dotnet_threadpool_num_threads`
6. **Time series — EF Core Query Duration**: query `rate(ef_query_duration_seconds_sum[5m]) / rate(ef_query_duration_seconds_count[5m])`
7. **Time series — EF Core Query Rate**: query `rate(ef_query_duration_seconds_count[5m])`

**Step 2: Verify and commit**

Same verification as Task 11.

```bash
git add monitoring/grafana/dashboards/app-performance.json
git commit -m "feat: add Application Performance Grafana dashboard"
```

---

### Task 13: Grafana Dashboard — Business Activity

**Files:**
- Create: `monitoring/grafana/dashboards/business-activity.json`

**Step 1: Create the Business Activity dashboard JSON**

Panels:
1. **Time series — CRUD Operations/min**: query `rate(xaf_object_crud_total[5m]) * 60` by entity and operation
2. **Stat panel — Total Customers**: query `xaf_object_crud_total{entity="Customer",operation="Create"}` minus deletes
3. **Stat panel — Total Orders**: similar
4. **Pie chart — Operations by Type**: query `sum(rate(xaf_object_crud_total[5m])) by (operation)`
5. **Time series — Login Activity**: query `rate(xaf_login_total[5m])` by result
6. **Bar gauge — CRUD by Entity**: query `sum(rate(xaf_object_crud_total[5m])) by (entity)`

**Step 2: Verify and commit**

```bash
git add monitoring/grafana/dashboards/business-activity.json
git commit -m "feat: add Business Activity Grafana dashboard"
```

---

### Task 14: Grafana Dashboard — SQL Server Health

**Files:**
- Create: `monitoring/grafana/dashboards/sql-server.json`

**Step 1: Create the SQL Server Health dashboard JSON**

Panels:
1. **Stat panel — Active Connections**: query `mssql_connections`
2. **Stat panel — Buffer Cache Hit Ratio**: query `mssql_buffer_cache_hit_ratio`
3. **Time series — Batch Requests/sec**: query `rate(mssql_batch_requests_total[5m])`
4. **Time series — Active Sessions**: query `mssql_sessions_active`
5. **Stat panel — Deadlocks**: query `mssql_deadlocks_total`
6. **Time series — Lock Wait Time**: query `rate(mssql_lock_wait_time_ms[5m])`
7. **Time series — IO Wait Time**: query `rate(mssql_io_wait_time_ms[5m])`
8. **Bar gauge — Database Size**: query `mssql_database_size_bytes` by database

**Step 2: Verify and commit**

```bash
git add monitoring/grafana/dashboards/sql-server.json
git commit -m "feat: add SQL Server Health Grafana dashboard"
```

---

### Task 15: End-to-End Verification

**Files:** None (verification only)

**Step 1: Start the full stack**

```bash
docker compose up -d
```

Verify all containers healthy:
```bash
docker compose ps
```
Expected: sqlserver, sql-exporter, prometheus, grafana all running.

**Step 2: Start the XAF app**

```bash
cd XafGraphana/XafGraphana.Blazor.Server
dotnet run
```

Wait 15 seconds for simulator to start and Prometheus to scrape.

**Step 3: Verify Prometheus targets**

Open: `http://localhost:9090/targets`
Expected: Both `xaf-app` and `sql-exporter` targets show UP (green).

**Step 4: Verify Grafana dashboards**

Open: `http://localhost:3000` (admin/admin)
Navigate to each dashboard in the "XAF Monitoring" folder:

1. **GAT Overview** — p95 response time updating, active sessions showing 0+, memory graph climbing
2. **Application Performance** — HTTP request heatmap populating, EF queries visible
3. **Business Activity** — CRUD counters incrementing (from simulator), operations chart showing mix of Create/Update/Delete
4. **SQL Server Health** — connections visible, buffer cache ratio near 1.0, batch requests flowing

**Step 5: Verify the XAF UI**

Open: `https://localhost:5001` — log in as Admin (empty password)
Navigate to Business → Customers: should see records being created by the simulator.
Navigate to Business → Orders: should see orders with various statuses.

**Step 6: Final commit**

If any adjustments were needed during verification, commit them:
```bash
git add -A
git commit -m "fix: adjustments from end-to-end verification"
```

---

## File Summary

### New files (13)
| # | File | Task |
|---|------|------|
| 1 | `docker-compose.yml` | 1 |
| 2 | `XafGraphana/XafGraphana.Module/BusinessObjects/Customer.cs` | 2 |
| 3 | `XafGraphana/XafGraphana.Module/BusinessObjects/Order.cs` | 2 |
| 4 | `XafGraphana/XafGraphana.Blazor.Server/Services/XafMetrics.cs` | 3 |
| 5 | `XafGraphana/XafGraphana.Blazor.Server/Services/EfCoreMetricsInterceptor.cs` | 4 |
| 6 | `XafGraphana/XafGraphana.Blazor.Server/Controllers/MetricsViewController.cs` | 6 |
| 7 | `XafGraphana/XafGraphana.Blazor.Server/Services/ActivitySimulatorService.cs` | 7 |
| 8 | `monitoring/prometheus/prometheus.yml` | 8 |
| 9 | `monitoring/sql-exporter/sql_exporter.yml` | 9 |
| 10 | `monitoring/grafana/provisioning/datasources/prometheus.yml` | 10 |
| 11 | `monitoring/grafana/provisioning/dashboards/dashboards.yml` | 10 |
| 12-15 | `monitoring/grafana/dashboards/*.json` (4 dashboards) | 11-14 |

### Modified files (5)
| File | Tasks |
|------|-------|
| `appsettings.Development.json` | 1 |
| `XafGraphanaDbContext.cs` | 2 |
| `Module.cs` | 2 |
| `Startup.cs` | 3, 4, 7 |
| `CircuitHandlerProxy.cs` | 5 |
| `XafGraphana.Blazor.Server.csproj` | 3 |
