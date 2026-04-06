# XafGrafana

POC: Full-stack observability for a DevExpress XAF Blazor Server application using Prometheus and Grafana.

## What This Proves

This POC answers the question: **does a Grafana/Prometheus monitoring stack add value to an XAF Blazor Server application?**

The focus is on producing evidence for **GAT (acceptance testing)** — proving SLA compliance, stability under load, and absence of memory leaks during extended test sessions.

## Architecture

```
Host machine
  ┌─────────────────────────────────┐
  │ XAF Blazor App (dotnet run)     │
  │   :5000                         │
  │   /metrics  ◄── Prometheus      │
  │   /monitoring  (embedded        │
  │                 Grafana iframes)│
  │   BackgroundService             │
  │   (simulated CRUD load)         │
  └─────────────────────────────────┘

Docker Compose
  ┌────────────┐ ┌──────────────┐ ┌───────────┐ ┌─────────┐
  │ SQL Server │ │ sql_exporter │ │Prometheus │ │ Grafana │
  │ :1433      │ │ :9399        │ │ :9090     │ │ :3001   │
  └────────────┘ └──────────────┘ └───────────┘ └─────────┘
```

## Tech Stack

- .NET 8, DevExpress XAF 25.2.3, EF Core
- prometheus-net (metrics endpoint)
- Prometheus (metrics collection)
- Grafana (dashboards)
- SQL Server 2022 (Docker)
- burningalchemist/sql_exporter (SQL Server metrics)

## Metrics Collected

### Application (automatic via prometheus-net)
- HTTP request duration, count, in-progress (by endpoint, status code)
- .NET GC collections, heap size, thread pool
- Process CPU, memory, uptime

### Application (custom)
- `xaf_object_crud_total` — CRUD operations by entity and operation type
- `xaf_active_sessions` — active Blazor circuit count
- `xaf_errors_total` — application errors by type
- `ef_query_duration_seconds` — EF Core query execution time histogram

### SQL Server (via sql_exporter)
- Active connections and sessions
- Batch requests/sec
- Buffer cache hit ratio
- Deadlocks, lock wait time, IO wait time
- Database size

## Grafana Dashboards

Four dashboards, accessible embedded in the XAF app at `/monitoring` or standalone at `http://localhost:3001`:

1. **GAT Overview** — P95 response time, error rate, active sessions, response time percentiles
2. **Application Performance** — requests/sec, memory, CPU, EF Core query duration
3. **Business Activity** — CRUD operations/min, entity counts, operations breakdown
4. **SQL Server Health** — connections, buffer cache, deadlocks, batch requests

## Quick Start

### Prerequisites
- .NET 8 SDK
- Docker Desktop
- DevExpress NuGet feed configured

### Steps

```bash
# 1. Start infrastructure
docker compose up -d

# 2. Update database schema
cd XafGrafana/XafGrafana.Blazor.Server
dotnet run -- --updateDatabase --forceUpdate --silent

# 3. Run the app
dotnet run --urls="http://localhost:5000"
```

### Access Points

| Service | URL | Credentials |
|---------|-----|-------------|
| XAF App | http://localhost:5000 | Admin / (empty) |
| Embedded Dashboards | http://localhost:5000/monitoring | — |
| Grafana (standalone) | http://localhost:3001 | admin / admin |
| Prometheus | http://localhost:9090 | — |
| Metrics endpoint | http://localhost:5000/metrics | — |

## Project Structure

```
XafGrafana/
├── docker-compose.yml
├── monitoring/
│   ├── prometheus/prometheus.yml
│   ├── sql-exporter/sql_exporter.yml
│   └── grafana/
│       ├── grafana.ini
│       ├── provisioning/
│       │   ├── datasources/prometheus.yml
│       │   └── dashboards/dashboards.yml
│       └── dashboards/
│           ├── gat-overview.json
│           ├── app-performance.json
│           ├── business-activity.json
│           └── sql-server.json
├── XafGrafana/
│   ├── XafGrafana.Blazor.Server/
│   │   ├── Startup.cs                    # Prometheus middleware + metrics registration
│   │   ├── Pages/Monitoring.razor        # Embedded Grafana iframe page
│   │   ├── Services/
│   │   │   ├── XafMetrics.cs             # Custom Prometheus metric definitions
│   │   │   ├── EfCoreMetricsInterceptor.cs
│   │   │   ├── ActivitySimulatorService.cs
│   │   │   └── CircuitHandlerProxy.cs    # Active sessions gauge
│   │   └── Controllers/
│   │       └── MetricsViewController.cs  # CRUD operation tracking
│   └── XafGrafana.Module/
│       ├── BusinessObjects/
│       │   ├── Customer.cs
│       │   ├── Order.cs
│       │   └── XafGrafanaDbContext.cs
│       └── Module.cs
└── docs/plans/
    ├── 2026-03-19-grafana-prometheus-poc-design.md
    └── 2026-03-19-grafana-prometheus-poc-implementation.md
```

## Migration Path

This POC uses **prometheus-net**. Migrating to **OpenTelemetry** later is straightforward:
- Custom metrics map 1:1 between libraries
- Prometheus + Grafana infrastructure stays identical
- OTel adds the option to also export traces and logs (Jaeger, Loki)

## Production Notes

Production target is Windows/IIS. For deployment:
- Install Prometheus + Grafana as Windows services, or run in Docker on the same box
- The XAF app just exposes `/metrics` — no containerization required
- Grafana dashboards are exportable as PDF/screenshots for GAT evidence
