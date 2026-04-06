# Stress Test Controllers Design

**Date:** 2026-04-06

## Goal

Add two XAF navigation views that let users generate controlled load and chaos scenarios to produce meaningful Grafana dashboard visualizations.

## Design Decisions

- **Two views**: Load Test (high-volume CRUD) and Chaos Test (degraded performance simulation)
- **Mutually exclusive**: only one runs at a time, managed by a shared `StressTestManager`
- **Preset-based**: Light / Medium / Heavy presets instead of fine-grained sliders
- **XAF nav items**: both views appear under a "Stress Testing" navigation group
- **Non-persistent objects**: parameter objects are not stored in the database

## Load Test

Generates high-volume Customer + Order CRUD operations.

| Preset | Ops/sec | Behavior |
|--------|---------|----------|
| Light | 5 | Single CRUD ops, mixed entity types |
| Medium | 20 | Single CRUD ops, higher frequency |
| Heavy | 50 | Bulk creates (batches of 10) |

Same operation mix as ActivitySimulatorService: 40% create customer, 30% create order, 20% update order, 10% delete cancelled order.

## Chaos Test

Simulates degraded performance scenarios.

| Preset | Scenarios Active | Frequency |
|--------|-----------------|-----------|
| Light | Slow queries only | Low (every 5s) |
| Medium | Slow queries + Error bursts | Moderate (every 2s) |
| Heavy | All three (+ Memory pressure) | High (every 1s) |

### Chaos Scenarios

1. **Slow queries** — Executes deliberately unoptimized queries (load all orders with nested includes, repeated enumeration). Spikes `ef_query_duration_seconds`.
2. **Error bursts** — Attempts invalid operations (null required fields, saving invalid state). Increments `xaf_errors_total`.
3. **Memory pressure** — Allocates large byte arrays, holds briefly, releases. Shows GC collection spikes and working set growth.

## Dashboard Impact

| Scenario | Affected Metrics |
|----------|-----------------|
| Load Test | `xaf_object_crud_total`, `ef_query_duration_seconds`, HTTP request metrics |
| Slow queries | `ef_query_duration_seconds` histogram shifts right, P95 response time |
| Error bursts | `xaf_errors_total` climbs |
| Memory pressure | `process_working_set_bytes`, GC collection count |

## Files

- `Services/StressTestManager.cs` — mutual exclusion coordinator
- `Services/LoadTestService.cs` — BackgroundService for CRUD load
- `Services/ChaosTestService.cs` — BackgroundService for chaos scenarios
- `Controllers/LoadTestViewController.cs` — XAF ViewController
- `Controllers/ChaosTestViewController.cs` — XAF ViewController
- `BusinessObjects/LoadTestParameters.cs` — non-persistent parameter object
- `BusinessObjects/ChaosTestParameters.cs` — non-persistent parameter object
- `Startup.cs` — service registration
