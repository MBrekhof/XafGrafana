# XafGrafana Test Project Design

## Goal

Add an xUnit test project as a **production template** for XAF EF Core testing. The tests themselves matter less than establishing reusable patterns for: ObjectSpace CRUD, ViewController testing, EF Core interceptors, and BackgroundService testing.

## Approach

Use `EFCoreObjectSpaceProvider<XafGrafanaEFCoreDbContext>` with EF Core InMemory provider for ObjectSpace tests. Use Moq for mocking IObjectSpace and INonSecuredObjectSpaceFactory where XAF infrastructure isn't needed.

## Test Categories

### 1. ObjectSpace CRUD Tests
- `CustomerObjectSpaceTests`, `OrderObjectSpaceTests`
- Use `EFCoreObjectSpaceProvider` with InMemory DB to create real `IObjectSpace` instances
- Test create, query, update, delete through IObjectSpace (not raw DbContext)
- Test Order-Customer relationship, OrderStatus transitions
- **Pattern:** how to bootstrap an ObjectSpaceProvider for tests

### 2. MetricsViewController Tests
- `MetricsViewControllerTests`
- Mock `IObjectSpace` with `ModifiedObjects`, `IsNewObject()`, `IsDeletedObject()`
- Fire `Committing` event and verify Prometheus counter labels (Create/Update/Delete)
- **Pattern:** how to test a ViewController without XAF UI

### 3. EfCoreMetricsInterceptor Tests
- `EfCoreMetricsInterceptorTests`
- Create DbContext with interceptor registered, execute queries
- Verify duration is observed on the histogram
- **Pattern:** how to test EF Core interceptors

### 4. ActivitySimulatorService Tests
- `ActivitySimulatorServiceTests`
- Mock `INonSecuredObjectSpaceFactory` and `IObjectSpace`
- Verify CRUD method calls and metric recording
- **Pattern:** how to test BackgroundServices using XAF ObjectSpace

## Project Structure

```
XafGrafana/XafGrafana.Tests/
  XafGrafana.Tests.csproj
  Infrastructure/
    ObjectSpaceTestBase.cs          -- shared EFCoreObjectSpaceProvider setup
  ObjectSpaceTests/
    CustomerObjectSpaceTests.cs
    OrderObjectSpaceTests.cs
  ControllerTests/
    MetricsViewControllerTests.cs
  ServiceTests/
    EfCoreMetricsInterceptorTests.cs
    ActivitySimulatorServiceTests.cs
```

## Dependencies

- xunit 2.9+
- xunit.runner.visualstudio
- Microsoft.NET.Test.Sdk
- Moq 4.20+
- FluentAssertions 7+
- DevExpress.ExpressApp.EFCore 25.2.5
- Microsoft.EntityFrameworkCore.InMemory 8.0.x (already in Module project)
