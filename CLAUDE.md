# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

POC for Prometheus/Grafana observability on a DevExpress XAF Blazor Server application. The host app collects metrics via prometheus-net and exposes them for a Docker-based monitoring stack.

## Tech Stack

- .NET 8 / C# 12, DevExpress XAF 25.2.5, EF Core 8, ASP.NET Core Blazor Server
- Monitoring: prometheus-net, Prometheus, Grafana, sql-exporter
- Database: SQL Server 2022 (Docker) or LocalDB (dev)

## Build & Run

```bash
# Build the solution
dotnet build XafGrafana/XafGrafana.Blazor.Server/XafGrafana.Blazor.Server.csproj

# Run the app (from Blazor.Server project dir)
dotnet run --project XafGrafana/XafGrafana.Blazor.Server --urls="http://localhost:5000"

# First-time database setup
dotnet run --project XafGrafana/XafGrafana.Blazor.Server -- --updateDatabase --forceUpdate --silent

# Start monitoring stack
docker compose up -d

# Run tests
dotnet test XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj
```

## Project Structure

- `XafGrafana/XafGrafana.Blazor.Server/` — Web app, Startup.cs, metrics services, Razor pages
- `XafGrafana/XafGrafana.Module/` — EF Core entities, business logic, XAF modules
- `monitoring/` — Docker configs: `prometheus/`, `grafana/` (dashboards + provisioning), `sql-exporter/`
- `XafGrafana/XafGrafana.Tests/` — xUnit tests (ObjectSpace CRUD, ViewController, interceptor, service)
- `HOW_TO_IMPLEMENT.md` — Step-by-step implementation guide (repo root)
- `docs/` — Architecture diagram, planning docs

## Key Architecture Decisions

- **EF Core metrics interceptor** uses a static list (`XafGrafanaEFCoreDbContext.ExternalInterceptors`) because XAF manages DbContext creation — you cannot use standard DI-based interceptor registration.
- **Blazor circuit tracking** uses a `CircuitHandlerProxy` wrapping XAF's `IScopedCircuitHandler` to track active sessions.
- **ActivitySimulatorService** generates fake CRUD operations for dashboard testing — runs continuously as a BackgroundService.
- Grafana is configured for anonymous access (`allow_embedding=true`) to support iframe embedding in the XAF app.

## Docker Monitoring Stack

Four services in docker-compose.yml:
- **sqlserver** (port 1433) — SQL Server 2022
- **sql-exporter** (port 9399) — SQL Server metrics for Prometheus
- **prometheus** (port 9090) — scrapes app at `host.docker.internal:5000/metrics` every 15s
- **grafana** (port 3000) — 4 dashboards: GAT Overview, Performance, Business Activity, SQL Server Health

## GitHub

- Remote: `https://github.com/MBrekhof/XafGrafana.git`
- GitHub CLI account: **MBrekhof** (not MartinWLN). Run `gh auth switch --user MBrekhof` if needed.

## Gotchas

- Connection strings are hardcoded in appsettings (dev-only POC) — `appsettings.json` for LocalDB, `appsettings.Development.json` for Docker SQL Server
- No EF Core migration files — schema updates use XAF's `--updateDatabase` approach
- Prometheus uses `host.docker.internal:5000` to reach the host app — this only works on Docker Desktop
- Test project uses `SnapshotChangeTrackingCustomizer` to override the DbContext's `ChangingAndChangedNotifications` strategy — InMemory provider rejects it because DevExpress built-in types don't implement `INotifyPropertyChanged`
- Test parallelization is disabled (`AssemblyInfo.cs`) because tests share static Prometheus metrics
