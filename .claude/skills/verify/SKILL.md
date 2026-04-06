---
name: verify
description: Build the solution and check Docker monitoring stack health. Use after making changes to verify everything works.
---

Run all verification steps below. Report results as a checklist. Stop and investigate on first failure.

## 1. Build

```bash
dotnet build XafGraphana/XafGraphana.Blazor.Server/XafGraphana.Blazor.Server.csproj --no-restore 2>&1 | tail -5
```

If restore is needed (new packages), run with restore:
```bash
dotnet build XafGraphana/XafGraphana.Blazor.Server/XafGraphana.Blazor.Server.csproj 2>&1 | tail -10
```

Report: build succeeded or failed (with error summary).

## 2. Docker Stack Health

Check all four monitoring services are running:

```bash
docker compose ps --format "table {{.Name}}\t{{.Status}}\t{{.Ports}}"
```

Expected: sqlserver, sql-exporter, prometheus, grafana — all "Up".

## 3. Service Health Checks

Run these in parallel:

- **Prometheus**: `curl -s -o /dev/null -w "%{http_code}" --max-time 5 http://localhost:9090/-/healthy`
- **Grafana**: `curl -s -o /dev/null -w "%{http_code}" --max-time 5 http://localhost:3000/api/health`
- **SQL Exporter**: `curl -s -o /dev/null -w "%{http_code}" --max-time 5 http://localhost:9399/metrics`

Report: HTTP status for each (200 = healthy).

## 4. Summary

Print a checklist:
- [ ] Solution builds
- [ ] Docker services running
- [ ] Prometheus healthy
- [ ] Grafana healthy
- [ ] SQL Exporter healthy
