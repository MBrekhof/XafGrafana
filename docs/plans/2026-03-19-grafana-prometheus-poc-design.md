# XAF Blazor + Grafana/Prometheus Observability POC

## Goal

Determine whether a Grafana/Prometheus monitoring stack adds value to an XAF Blazor Server application, with a focus on producing evidence for GAT (acceptance testing) — proving SLA compliance, stability under load, and absence of memory leaks.

## Approach

**Approach A: prometheus-net + Prometheus + Grafana**

- Minimal code changes, industry standard, large ecosystem of pre-built dashboards
- Migration path to OpenTelemetry is straightforward if needed later
- Production target: Windows/IIS (all-in-one server with Hangfire etc.)

## Architecture

```
Host machine (dev box)

  ┌───────────────────────────┐
  │ XAF Blazor App            │
  │ (dotnet run, port 5000)   │
  │                           │
  │  /metrics ◄───────────────┼──── Prometheus scrapes every 15s
  │                           │
  │  BackgroundService        │
  │  (simulates CRUD load)    │
  └───────────────────────────┘

  Docker Compose
  ┌─────────────┐ ┌───────────┐ ┌─────────────────┐
  │ SQL Server   │ │Prometheus │ │ Grafana         │
  │ :1433        │ │ :9090     │ │ :3000           │
  └─────────────┘ └───────────┘ └─────────────────┘
  ┌──────────────────────────────────────────────────┐
  │ sql_exporter (scrapes SQL Server DMVs)           │
  └──────────────────────────────────────────────────┘
```

Prometheus reaches XAF app via `host.docker.internal:5000`.

## Metrics Collected

### App-level (automatic via prometheus-net)

- HTTP request duration & count (by endpoint, status code)
- Active HTTP connections, requests in progress
- .NET GC collection counts & heap size
- Thread pool queue length & thread count
- Process CPU & memory usage

### App-level (custom)

- `xaf_object_crud_total` — counter by entity name + operation (Create/Read/Update/Delete)
- `xaf_login_total` — counter by success/failure
- `xaf_active_sessions` — gauge of active Blazor circuits
- `ef_query_duration_seconds` — histogram of EF Core query execution times

### GAT-specific

- `xaf_request_duration_p95` — 95th percentile response times (via histogram buckets)
- `xaf_concurrent_users` — simultaneous active Blazor circuits over time
- `xaf_errors_total` — counter by error type (unhandled exceptions, 500s, timeouts)
- `dotnet_memory_working_set_bytes` — memory over time (leak detection)
- `xaf_gc_pause_ratio` — GC pause time relative to uptime
- `sql_deadlock_total` / `sql_lock_wait_time` — database stability under load

### SQL Server (via sql_exporter)

- Active connections / sessions
- Queries per second
- Buffer cache hit ratio
- Wait stats (locks, IO, memory)
- Database size

## Business Objects

### Customer

- `Name`, `Email`, `City`
- Standard XAF entity with `BaseObjectInt`

### Order

- `OrderDate`, `Amount`, `Status` (enum: New/Processing/Shipped/Delivered/Cancelled)
- Navigation to `Customer`

## Load Simulation

`ActivitySimulatorService` (hosted `BackgroundService`):

- Creates customers at random intervals (every 5-15s)
- Creates orders linked to random existing customers
- Updates order statuses (lifecycle progression)
- Occasionally deletes cancelled orders
- Logs in/out via API endpoint (auth metrics)
- Varies intensity over time for realistic graphs
- Uses `IObjectSpaceFactory` so all activity flows through XAF pipeline

## Grafana Dashboards

1. **GAT Overview** — key SLAs: p95 response time, error rate, uptime, concurrent users
2. **Application Performance** — HTTP latency heatmap, throughput, memory/CPU trends
3. **Business Activity** — CRUD rates, login activity, entity counts
4. **SQL Server Health** — query performance, connections, waits, cache efficiency

Dashboards exportable as screenshots/PDFs for GAT evidence.

## Docker Compose

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| sqlserver | mcr.microsoft.com/mssql/server:2022-latest | 1433 | Database |
| sql-exporter | burningalchemist/sql_exporter | 9399 | SQL Server metrics |
| prometheus | prom/prometheus | 9090 | Metrics collection |
| grafana | grafana/grafana | 3000 | Dashboards |

### File structure

```
monitoring/
├── prometheus/
│   └── prometheus.yml
├── grafana/
│   ├── provisioning/
│   │   ├── datasources/
│   │   │   └── prometheus.yml
│   │   └── dashboards/
│   │       └── dashboards.yml
│   └── dashboards/
│       ├── gat-overview.json
│       ├── app-performance.json
│       ├── business-activity.json
│       └── sql-server.json
└── sql-exporter/
    └── sql_exporter.yml
```

## Code Changes

### New NuGet packages (Blazor.Server)

- `prometheus-net.AspNetCore`
- `prometheus-net`

### New files

| File | Project | Purpose |
|------|---------|---------|
| `Services/XafMetrics.cs` | Blazor.Server | Custom Prometheus counters/gauges/histograms |
| `Services/ActivitySimulatorService.cs` | Blazor.Server | BackgroundService generating CRUD load |
| `Services/EfCoreMetricsInterceptor.cs` | Blazor.Server | EF Core interceptor for query duration |
| `BusinessObjects/Customer.cs` | Module | Customer entity |
| `BusinessObjects/Order.cs` | Module | Order entity |
| `Controllers/MetricsViewController.cs` | Module | XAF controller incrementing CRUD counters |

### Modified files

| File | Change |
|------|--------|
| `Startup.cs` | Register Prometheus middleware, metrics endpoint, hosted service |
| `XafGrafanaDbContext.cs` | Add DbSet<Customer>, DbSet<Order>, register interceptor |
| `Module.cs` | Export new types |
| `appsettings.Development.json` | Connection string → Docker SQL Server |

## Connection String (Development)

```
Server=localhost,1433;Database=XafGrafana;User=sa;Password=<SA_PASSWORD>;TrustServerCertificate=True
```
