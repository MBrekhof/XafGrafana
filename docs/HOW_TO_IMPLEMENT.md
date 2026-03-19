# How to Add Prometheus/Grafana Monitoring to an Existing XAF Blazor Server Application

This guide explains how to take the monitoring setup from this POC and apply it to your own XAF Blazor Server application. Each section is self-contained — you can adopt parts independently.

---

## Table of Contents

1. [Add the /metrics Endpoint](#1-add-the-metrics-endpoint)
2. [Define Custom Metrics](#2-define-custom-metrics)
3. [Track EF Core Query Performance](#3-track-ef-core-query-performance)
4. [Track Active Blazor Sessions](#4-track-active-blazor-sessions)
5. [Track CRUD Operations](#5-track-crud-operations)
6. [Set Up Docker Infrastructure](#6-set-up-docker-infrastructure)
7. [Add SQL Server Monitoring](#7-add-sql-server-monitoring)
8. [Create Grafana Dashboards](#8-create-grafana-dashboards)
9. [Embed Dashboards in the XAF App](#9-embed-dashboards-in-the-xaf-app)
10. [Production Deployment on Windows/IIS](#10-production-deployment-on-windowiis)
11. [Gotchas and Lessons Learned](#11-gotchas-and-lessons-learned)

---

## 1. Add the /metrics Endpoint

This is the foundation — prometheus-net exposes a `/metrics` endpoint that Prometheus scrapes.

### Install NuGet package

```bash
cd YourApp.Blazor.Server
dotnet add package prometheus-net.AspNetCore
```

This pulls in `prometheus-net` as a transitive dependency.

### Modify Startup.cs

Add the using:

```csharp
using Prometheus;
```

In `ConfigureServices`, add health checks (optional but useful):

```csharp
services.AddHealthChecks();
```

In `Configure`, add HTTP metrics middleware **after** `UseRouting()` and **before** `UseAuthentication()`:

```csharp
app.UseRouting();
app.UseHttpMetrics();   // <-- add this
app.UseAuthentication();
```

In the `UseEndpoints` block, map the metrics and health endpoints:

```csharp
app.UseEndpoints(endpoints =>
{
    endpoints.MapXafEndpoints();
    endpoints.MapBlazorHub();
    endpoints.MapFallbackToPage("/_Host");
    endpoints.MapMetrics();              // <-- add this
    endpoints.MapHealthChecks("/health"); // <-- add this
    endpoints.MapControllers();
});
```

### Verify

Run the app and navigate to `http://localhost:5000/metrics`. You should see Prometheus text format with `http_request_duration_seconds`, `dotnet_collection_count_total`, `process_cpu_seconds_total`, etc.

---

## 2. Define Custom Metrics

Create a static class to hold your application-specific Prometheus metrics.

### Create `Services/XafMetrics.cs`

```csharp
using Prometheus;

namespace YourApp.Blazor.Server.Services;

public static class XafMetrics
{
    // CRUD operations — labels: entity name, operation type
    public static readonly Counter ObjectCrudTotal = Metrics.CreateCounter(
        "xaf_object_crud_total",
        "Total XAF object CRUD operations",
        new CounterConfiguration { LabelNames = new[] { "entity", "operation" } });

    // Login attempts — labels: success/failure
    public static readonly Counter LoginTotal = Metrics.CreateCounter(
        "xaf_login_total",
        "Total login attempts",
        new CounterConfiguration { LabelNames = new[] { "result" } });

    // Active Blazor circuits (sessions)
    public static readonly Gauge ActiveSessions = Metrics.CreateGauge(
        "xaf_active_sessions",
        "Number of active Blazor circuits");

    // Application errors — labels: error type
    public static readonly Counter ErrorsTotal = Metrics.CreateCounter(
        "xaf_errors_total",
        "Total application errors",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    // EF Core query duration
    public static readonly Histogram EfQueryDuration = Metrics.CreateHistogram(
        "ef_query_duration_seconds",
        "EF Core query execution duration in seconds",
        new HistogramConfiguration
        {
            // Buckets from 1ms to ~16s
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 15)
        });
}
```

### Adding your own metrics

prometheus-net supports three metric types:

- **Counter** — monotonically increasing value (requests, errors, operations)
- **Gauge** — value that goes up and down (active sessions, queue depth)
- **Histogram** — distribution of values in buckets (response times, query durations)

Use **labels** to add dimensions. Example:

```csharp
// Increment a counter with labels
XafMetrics.ObjectCrudTotal.WithLabels("Customer", "Create").Inc();

// Set a gauge
XafMetrics.ActiveSessions.Inc();   // +1
XafMetrics.ActiveSessions.Dec();   // -1

// Observe a histogram value
XafMetrics.EfQueryDuration.Observe(0.042); // 42ms
```

---

## 3. Track EF Core Query Performance

An EF Core `DbCommandInterceptor` captures query execution times.

### Create `Services/EfCoreMetricsInterceptor.cs`

```csharp
#nullable enable
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace YourApp.Blazor.Server.Services;

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

### Register with XAF's DbContext

XAF manages DbContext creation, so standard EF Core DI-based interceptor registration doesn't work. Instead, add a static interceptor list to your DbContext:

In your `XafGraphanaEFCoreDbContext.cs` (Module project):

```csharp
using Microsoft.EntityFrameworkCore.Diagnostics;

// Add to the class:
public static readonly List<IInterceptor> ExternalInterceptors = new();

protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    base.OnConfiguring(optionsBuilder);
    if (ExternalInterceptors.Count > 0)
    {
        optionsBuilder.AddInterceptors(ExternalInterceptors);
    }
}
```

In `Startup.cs`, register the interceptor **before** `services.AddXaf(...)`:

```csharp
YourDbContext.ExternalInterceptors.Add(new EfCoreMetricsInterceptor());
```

---

## 4. Track Active Blazor Sessions

XAF Blazor apps already have a `CircuitHandlerProxy`. Add gauge tracking to it.

### Modify `Services/CircuitHandlerProxy.cs`

```csharp
public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
{
    XafMetrics.ActiveSessions.Inc();     // <-- add this
    return scopedCircuitHandler.OnCircuitOpenedAsync(cancellationToken);
}

public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
{
    XafMetrics.ActiveSessions.Dec();     // <-- add this
    return scopedCircuitHandler.OnCircuitClosedAsync(cancellationToken);
}
```

---

## 5. Track CRUD Operations

A XAF ViewController hooks into `ObjectSpace.Committing` to count create/update/delete operations.

### Create `Controllers/MetricsViewController.cs` (Blazor.Server project)

```csharp
using DevExpress.ExpressApp;

namespace YourApp.Blazor.Server.Controllers;

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

            if (objectSpace.IsNewObject(obj))
                XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Create").Inc();
            else if (objectSpace.IsDeletedObject(obj))
                XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Delete").Inc();
            else
                XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Update").Inc();
        }
    }
}
```

**Important:** This controller must go in the Blazor.Server project (not Module) because it references `XafMetrics` which depends on prometheus-net.

**Important:** Use `IsNewObject()` / `IsDeletedObject()` — not `GetObjectState()` / `ObjectState` enum. Those don't exist on XAF's EF Core `IObjectSpace`.

---

## 6. Set Up Docker Infrastructure

### docker-compose.yml

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: your-sqlserver
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

  prometheus:
    image: prom/prometheus:latest
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

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3001:3000"
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: admin
      GF_USERS_ALLOW_SIGN_UP: "false"
    volumes:
      - ./monitoring/grafana/grafana.ini:/etc/grafana/grafana.ini:ro
      - ./monitoring/grafana/provisioning:/etc/grafana/provisioning:ro
      - ./monitoring/grafana/dashboards:/var/lib/grafana/dashboards:ro
      - grafana-data:/var/lib/grafana
    depends_on:
      - prometheus

volumes:
  sqlserver-data:
  prometheus-data:
  grafana-data:
```

### monitoring/prometheus/prometheus.yml

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: "xaf-app"
    metrics_path: /metrics
    scheme: http
    static_configs:
      - targets: ["host.docker.internal:5000"]
```

Prometheus reaches the locally-running XAF app via `host.docker.internal`.

### monitoring/grafana/grafana.ini

```ini
[auth.anonymous]
enabled = true
org_role = Viewer

[security]
allow_embedding = true

[users]
default_theme = dark
```

Anonymous access and `allow_embedding` are required for the iframe embedding to work.

### monitoring/grafana/provisioning/datasources/prometheus.yml

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

**Do not** set a `uid` on the datasource — let Grafana auto-assign it. In dashboard JSON files, reference the datasource by type only: `{"type": "prometheus"}`.

### monitoring/grafana/provisioning/dashboards/dashboards.yml

```yaml
apiVersion: 1
providers:
  - name: "YourApp"
    orgId: 1
    folder: "XAF Monitoring"
    type: file
    disableDeletion: false
    editable: true
    options:
      path: /var/lib/grafana/dashboards
      foldersFromFilesStructure: false
```

---

## 7. Add SQL Server Monitoring

### Add sql_exporter to docker-compose.yml

```yaml
  sql-exporter:
    image: burningalchemist/sql_exporter:latest
    command: ["--config.file=/etc/sql_exporter/sql_exporter.yml"]
    ports:
      - "9399:9399"
    volumes:
      - ./monitoring/sql-exporter/sql_exporter.yml:/etc/sql_exporter/sql_exporter.yml:ro
    depends_on:
      sqlserver:
        condition: service_healthy
```

**Important:** The `command` flag is required — the default entrypoint looks for `sql_exporter.yml` in the working directory, not `/etc/`.

### Add scrape target to prometheus.yml

```yaml
  - job_name: "sql-exporter"
    static_configs:
      - targets: ["sql-exporter:9399"]
```

### monitoring/sql-exporter/sql_exporter.yml

The config needs a `collectors` reference in the `target` section — without it, the exporter starts but reports "no collectors defined":

```yaml
global:
  scrape_timeout_offset: 500ms
  min_interval: 0s
  max_connections: 3
  max_idle_connections: 3

target:
  data_source_name: "sqlserver://sa:YourStr0ng!Passw0rd@sqlserver:1433?encrypt=disable"
  collectors: [mssql_standard]    # <-- required!

collectors:
  - collector_name: mssql_standard
    metrics:
      - metric_name: mssql_connections
        type: gauge
        help: "Number of active connections"
        values: [count]
        query: |
          SELECT COUNT(*) AS count FROM sys.dm_exec_connections

      # ... add more metrics as needed
      # See monitoring/sql-exporter/sql_exporter.yml in this repo for the full list
```

---

## 8. Create Grafana Dashboards

Dashboard JSON files go in `monitoring/grafana/dashboards/`. They are auto-loaded by the provisioning config.

### Key rules for provisioned dashboard JSON

1. **Set a stable `uid`** at the top level (e.g., `"uid": "xaf-gat-overview"`) — this is what the iframe URL references
2. **Reference datasource by type only** — use `{"type": "prometheus"}` without a `uid` field. Grafana will use the default datasource of that type
3. **Set `schemaVersion: 39`** or higher
4. **Validate your JSON** — trailing commas will crash Grafana's provisioning entirely

### Dashboard structure template

```json
{
  "uid": "your-dashboard-uid",
  "title": "Your Dashboard",
  "schemaVersion": 39,
  "refresh": "15s",
  "time": { "from": "now-1h", "to": "now" },
  "panels": [
    {
      "id": 1,
      "title": "Panel Title",
      "type": "timeseries",
      "gridPos": { "x": 0, "y": 0, "w": 24, "h": 8 },
      "datasource": { "type": "prometheus" },
      "targets": [
        {
          "datasource": { "type": "prometheus" },
          "expr": "your_prometheus_query_here",
          "legendFormat": "{{label}}"
        }
      ],
      "fieldConfig": {
        "defaults": {
          "unit": "reqps"
        },
        "overrides": []
      }
    }
  ]
}
```

### Useful PromQL queries for XAF

| Metric | PromQL |
|--------|--------|
| P95 response time | `histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))` |
| Requests/sec | `sum(rate(http_request_duration_seconds_count[5m]))` |
| CRUD ops/min | `sum(rate(xaf_object_crud_total[5m])) by (entity, operation) * 60` |
| Avg EF query time | `rate(ef_query_duration_seconds_sum[5m]) / rate(ef_query_duration_seconds_count[5m])` |
| Memory | `process_working_set_bytes` |
| CPU % | `rate(process_cpu_seconds_total[5m]) * 100` |

---

## 9. Embed Dashboards in the XAF App

### Create `Pages/Monitoring.razor`

```razor
@page "/monitoring"

<div class="monitoring-page">
    <div class="monitoring-tabs">
        @foreach (var tab in dashboards)
        {
            <button class="tab-button @(activeTab == tab.Key ? "active" : "")"
                    @onclick="() => activeTab = tab.Key">
                @tab.Value.title
            </button>
        }
    </div>
    <div class="dashboard-container">
        <iframe src="@GetDashboardUrl()"
                class="grafana-iframe"
                title="Grafana Dashboard"
                frameborder="0" />
    </div>
</div>

@code {
    private string activeTab = "gat";
    private string grafanaBaseUrl = "http://localhost:3001";

    private readonly Dictionary<string, (string uid, string title)> dashboards = new()
    {
        ["gat"] = ("xaf-gat-overview", "GAT Overview"),
        ["perf"] = ("xaf-app-performance", "Application Performance"),
        ["biz"] = ("xaf-business-activity", "Business Activity"),
        ["sql"] = ("xaf-sql-server", "SQL Server Health"),
    };

    private string GetDashboardUrl()
    {
        var uid = dashboards[activeTab].uid;
        return $"{grafanaBaseUrl}/d/{uid}?kiosk&theme=dark&refresh=15s";
    }
}

<style>
    .monitoring-page { display: flex; flex-direction: column; height: calc(100vh - 60px); }
    .monitoring-tabs { display: flex; padding: 8px 16px 0; border-bottom: 1px solid #dee2e6; }
    .tab-button { padding: 10px 20px; border: none; background: transparent; cursor: pointer; border-radius: 6px 6px 0 0; }
    .tab-button.active { background: #fff; color: #0d6efd; font-weight: 500; border: 1px solid #dee2e6; border-bottom: none; }
    .dashboard-container { flex: 1; }
    .grafana-iframe { width: 100%; height: 100%; border: none; min-height: 600px; }
</style>
```

The `?kiosk` parameter hides Grafana's navigation chrome. `&theme=dark` forces dark mode.

### Grafana requirements for embedding

In `grafana.ini`, these settings must be enabled:

```ini
[auth.anonymous]
enabled = true

[security]
allow_embedding = true
```

### Accessing the page

Navigate to `http://localhost:5000/monitoring`. This is a standalone Blazor page outside of XAF's security — no login required.

---

## 10. Production Deployment on Windows/IIS

For a Windows/IIS deployment (the target for XAF apps), the monitoring stack runs alongside the app:

### Option A: Prometheus + Grafana as Windows Services

1. Download Prometheus and Grafana Windows binaries
2. Install as Windows services using `nssm` or `WinSW`
3. Prometheus scrapes the XAF app on `localhost:5000/metrics`
4. Grafana connects to Prometheus on `localhost:9090`
5. Update the Grafana base URL in `Monitoring.razor` to match

### Option B: Prometheus + Grafana in Docker on the same server

1. Install Docker Desktop on the Windows server
2. Use the same `docker-compose.yml` (without SQL Server — use your real database)
3. Prometheus scrapes via `host.docker.internal`

### Connection string

Update `appsettings.json` to point at your real SQL Server instead of the Docker instance:

```json
{
  "ConnectionStrings": {
    "ConnectionString": "Server=your-server;Database=YourDb;Integrated Security=SSPI;..."
  }
}
```

### Remove the activity simulator

Remove `services.AddHostedService<ActivitySimulatorService>();` from `Startup.cs` — the simulator is only for the POC.

---

## 11. Gotchas and Lessons Learned

### XAF-specific

| Issue | Solution |
|-------|----------|
| EF Core interceptors don't register via DI with XAF | Use static `ExternalInterceptors` list on DbContext + `OnConfiguring` |
| `ObjectSpace.GetObjectState()` doesn't exist in EF Core | Use `IsNewObject()` / `IsDeletedObject()` instead |
| Collection properties need `ObservableCollection<T>` | XAF's `ChangingAndChangedNotificationsWithOriginalValues` strategy requires it — `List<T>` throws at runtime |
| `MetricsViewController` must be in Blazor.Server project | Module project should not reference prometheus-net |

### Grafana

| Issue | Solution |
|-------|----------|
| "Dashboard not found" in iframe | Set stable `uid` in dashboard JSON + verify Grafana loaded them via API |
| "Datasource provisioning error: data source not found" | Do NOT set `uid` in datasource provisioning — let Grafana auto-assign |
| Dashboards reference wrong datasource | Use `{"type": "prometheus"}` without `uid` — Grafana uses the default of that type |
| Invalid JSON crashes all of Grafana provisioning | Validate JSON before deploying — one trailing comma kills all dashboards |
| Port conflict | Check that Grafana port (default 3000) doesn't conflict with other services |

### sql_exporter

| Issue | Solution |
|-------|----------|
| "no such file or directory" | Add `command: ["--config.file=/etc/sql_exporter/sql_exporter.yml"]` |
| "no collectors defined for target" | Add `collectors: [collector_name]` to the `target:` section in config |

### Decimal properties

| Issue | Solution |
|-------|----------|
| EF Core warns about unspecified precision for `decimal` | Add `[Precision(18, 2)]` attribute (from `Microsoft.EntityFrameworkCore`) |
