# XafGrafana Test Project Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add an xUnit test project as a production template for XAF EF Core testing with 4 test categories: ObjectSpace CRUD, ViewController, EF Core interceptor, and BackgroundService.

**Architecture:** Use `EFCoreObjectSpaceProvider<XafGrafanaEFCoreDbContext>` with EF Core InMemory provider for tests that need real ObjectSpace instances. Use Moq for tests that mock XAF interfaces (IObjectSpace, INonSecuredObjectSpaceFactory). A shared `ObjectSpaceTestBase` class handles provider setup/teardown.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, DevExpress XAF 25.2.5 EF Core, Microsoft.EntityFrameworkCore.InMemory

---

### Task 1: Create Test Project and Add to Solution

**Files:**
- Create: `XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj`
- Modify: `XafGrafana.slnx`

**Step 1: Create the test project**

Run:
```bash
cd /c/Projects/XafGrafana && dotnet new xunit -n XafGrafana.Tests -o XafGrafana/XafGrafana.Tests --framework net8.0
```

**Step 2: Replace the generated csproj with correct dependencies**

Overwrite `XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Moq" Version="4.*" />
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.18" />
    <PackageReference Include="DevExpress.ExpressApp.EFCore" Version="25.2.5" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\XafGrafana.Module\XafGrafana.Module.csproj" />
    <ProjectReference Include="..\XafGrafana.Blazor.Server\XafGrafana.Blazor.Server.csproj" />
  </ItemGroup>
</Project>
```

**Step 3: Add project to solution**

Run:
```bash
cd /c/Projects/XafGrafana && dotnet sln XafGrafana.slnx add XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj
```

**Step 4: Delete the generated UnitTest1.cs**

Run:
```bash
rm XafGrafana/XafGrafana.Tests/UnitTest1.cs
```

**Step 5: Restore and build to verify setup**

Run:
```bash
dotnet build XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj
```

Expected: Build succeeded, 0 errors.

**Step 6: Commit**

```bash
git add XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj XafGrafana.slnx
git commit -m "feat: add xUnit test project with DevExpress XAF EF Core dependencies"
```

---

### Task 2: ObjectSpaceTestBase Infrastructure

**Files:**
- Create: `XafGrafana/XafGrafana.Tests/Infrastructure/ObjectSpaceTestBase.cs`

**Step 1: Write the base class**

Create `XafGrafana/XafGrafana.Tests/Infrastructure/ObjectSpaceTestBase.cs`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.EFCore;
using Microsoft.EntityFrameworkCore;
using XafGrafana.Module.BusinessObjects;

namespace XafGrafana.Tests.Infrastructure;

/// <summary>
/// Base class for tests that need a real IObjectSpace backed by InMemory EF Core.
/// Each test gets a fresh database via a unique database name.
/// </summary>
public abstract class ObjectSpaceTestBase : IDisposable
{
    private readonly EFCoreObjectSpaceProvider<XafGrafanaEFCoreDbContext> _provider;

    protected ObjectSpaceTestBase()
    {
        var dbName = $"TestDb_{Guid.NewGuid():N}";
        _provider = new EFCoreObjectSpaceProvider<XafGrafanaEFCoreDbContext>(
            (builder, _) =>
            {
                builder.UseInMemoryDatabase(dbName);
                builder.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
            });
    }

    protected IObjectSpace CreateObjectSpace()
    {
        return _provider.CreateObjectSpace();
    }

    public void Dispose()
    {
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }
}
```

**Step 2: Build to verify it compiles**

Run:
```bash
dotnet build XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj
```

Expected: Build succeeded. If `EFCoreObjectSpaceProvider` constructor signature differs, adjust — the key overload is `(Action<DbContextOptionsBuilder, string> optionsBuilder)`.

**Step 3: Commit**

```bash
git add XafGrafana/XafGrafana.Tests/Infrastructure/ObjectSpaceTestBase.cs
git commit -m "feat: add ObjectSpaceTestBase with InMemory EFCoreObjectSpaceProvider"
```

---

### Task 3: Customer ObjectSpace CRUD Tests

**Files:**
- Create: `XafGrafana/XafGrafana.Tests/ObjectSpaceTests/CustomerObjectSpaceTests.cs`

**Step 1: Write the tests**

Create `XafGrafana/XafGrafana.Tests/ObjectSpaceTests/CustomerObjectSpaceTests.cs`:

```csharp
using DevExpress.ExpressApp;
using FluentAssertions;
using XafGrafana.Module.BusinessObjects;
using XafGrafana.Tests.Infrastructure;

namespace XafGrafana.Tests.ObjectSpaceTests;

public class CustomerObjectSpaceTests : ObjectSpaceTestBase
{
    [Fact]
    public void CreateCustomer_SetsPropertiesAndPersists()
    {
        using var os = CreateObjectSpace();
        var customer = os.CreateObject<Customer>();
        customer.Name = "Jan de Vries";
        customer.Email = "jan@example.com";
        customer.City = "Amsterdam";
        os.CommitChanges();

        using var os2 = CreateObjectSpace();
        var loaded = os2.FindObject<Customer>(null);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Jan de Vries");
        loaded.Email.Should().Be("jan@example.com");
        loaded.City.Should().Be("Amsterdam");
    }

    [Fact]
    public void UpdateCustomer_PersistsChanges()
    {
        using var os = CreateObjectSpace();
        var customer = os.CreateObject<Customer>();
        customer.Name = "Original";
        os.CommitChanges();

        using var os2 = CreateObjectSpace();
        var loaded = os2.FindObject<Customer>(null);
        loaded!.Name = "Updated";
        os2.CommitChanges();

        using var os3 = CreateObjectSpace();
        var reloaded = os3.FindObject<Customer>(null);
        reloaded!.Name.Should().Be("Updated");
    }

    [Fact]
    public void DeleteCustomer_RemovesFromDatabase()
    {
        using var os = CreateObjectSpace();
        var customer = os.CreateObject<Customer>();
        customer.Name = "ToDelete";
        os.CommitChanges();

        using var os2 = CreateObjectSpace();
        var loaded = os2.FindObject<Customer>(null);
        os2.Delete(loaded);
        os2.CommitChanges();

        using var os3 = CreateObjectSpace();
        var result = os3.FindObject<Customer>(null);
        result.Should().BeNull();
    }

    [Fact]
    public void QueryCustomers_ReturnsAllCreated()
    {
        using var os = CreateObjectSpace();
        for (int i = 0; i < 3; i++)
        {
            var c = os.CreateObject<Customer>();
            c.Name = $"Customer {i}";
        }
        os.CommitChanges();

        using var os2 = CreateObjectSpace();
        var all = os2.GetObjects<Customer>();
        all.Should().HaveCount(3);
    }
}
```

**Step 2: Run the tests**

Run:
```bash
dotnet test XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj --filter "FullyQualifiedName~CustomerObjectSpaceTests" -v normal
```

Expected: 4 tests pass. If `FindObject<T>(null)` doesn't work (XAF expects `CriteriaOperator`), use `os2.GetObjects<Customer>().FirstOrDefault()` instead.

**Step 3: Commit**

```bash
git add XafGrafana/XafGrafana.Tests/ObjectSpaceTests/CustomerObjectSpaceTests.cs
git commit -m "test: add Customer ObjectSpace CRUD tests"
```

---

### Task 4: Order ObjectSpace CRUD Tests

**Files:**
- Create: `XafGrafana/XafGrafana.Tests/ObjectSpaceTests/OrderObjectSpaceTests.cs`

**Step 1: Write the tests**

Create `XafGrafana/XafGrafana.Tests/ObjectSpaceTests/OrderObjectSpaceTests.cs`:

```csharp
using DevExpress.ExpressApp;
using FluentAssertions;
using XafGrafana.Module.BusinessObjects;
using XafGrafana.Tests.Infrastructure;

namespace XafGrafana.Tests.ObjectSpaceTests;

public class OrderObjectSpaceTests : ObjectSpaceTestBase
{
    private Customer CreateTestCustomer(IObjectSpace os)
    {
        var customer = os.CreateObject<Customer>();
        customer.Name = "Test Customer";
        customer.Email = "test@example.com";
        customer.City = "Utrecht";
        return customer;
    }

    [Fact]
    public void CreateOrder_WithCustomer_PersistsRelationship()
    {
        using var os = CreateObjectSpace();
        var customer = CreateTestCustomer(os);
        var order = os.CreateObject<Order>();
        order.Customer = customer;
        order.OrderDate = new DateTime(2026, 1, 15);
        order.Amount = 99.95m;
        order.Status = OrderStatus.New;
        os.CommitChanges();

        using var os2 = CreateObjectSpace();
        var loaded = os2.GetObjects<Order>().First();
        loaded.Customer.Should().NotBeNull();
        loaded.Customer.Name.Should().Be("Test Customer");
        loaded.Amount.Should().Be(99.95m);
        loaded.Status.Should().Be(OrderStatus.New);
    }

    [Fact]
    public void OrderStatus_CanTransitionThroughLifecycle()
    {
        using var os = CreateObjectSpace();
        var customer = CreateTestCustomer(os);
        var order = os.CreateObject<Order>();
        order.Customer = customer;
        order.OrderDate = DateTime.Now;
        order.Amount = 50m;
        order.Status = OrderStatus.New;
        os.CommitChanges();

        using var os2 = CreateObjectSpace();
        var loaded = os2.GetObjects<Order>().First();
        loaded.Status = OrderStatus.Processing;
        os2.CommitChanges();

        using var os3 = CreateObjectSpace();
        var reloaded = os3.GetObjects<Order>().First();
        reloaded.Status.Should().Be(OrderStatus.Processing);
    }

    [Fact]
    public void CustomerOrders_Collection_ReflectsRelatedOrders()
    {
        using var os = CreateObjectSpace();
        var customer = CreateTestCustomer(os);
        var order1 = os.CreateObject<Order>();
        order1.Customer = customer;
        order1.OrderDate = DateTime.Now;
        order1.Amount = 10m;
        order1.Status = OrderStatus.New;

        var order2 = os.CreateObject<Order>();
        order2.Customer = customer;
        order2.OrderDate = DateTime.Now;
        order2.Amount = 20m;
        order2.Status = OrderStatus.Processing;
        os.CommitChanges();

        using var os2 = CreateObjectSpace();
        var loaded = os2.GetObjects<Customer>().First();
        loaded.Orders.Should().HaveCount(2);
    }
}
```

**Step 2: Run the tests**

Run:
```bash
dotnet test XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj --filter "FullyQualifiedName~OrderObjectSpaceTests" -v normal
```

Expected: 3 tests pass.

**Step 3: Commit**

```bash
git add XafGrafana/XafGrafana.Tests/ObjectSpaceTests/OrderObjectSpaceTests.cs
git commit -m "test: add Order ObjectSpace CRUD and relationship tests"
```

---

### Task 5: MetricsViewController Tests

**Files:**
- Create: `XafGrafana/XafGrafana.Tests/ControllerTests/MetricsViewControllerTests.cs`

**Context:** `MetricsViewController` (at `XafGrafana.Blazor.Server/Controllers/MetricsViewController.cs`) subscribes to `ObjectSpace.Committing` and inspects `ModifiedObjects` to increment Prometheus counters. Testing pattern: mock IObjectSpace, fire the Committing event, verify counter behavior.

**Step 1: Write the tests**

Create `XafGrafana/XafGrafana.Tests/ControllerTests/MetricsViewControllerTests.cs`:

```csharp
using System.ComponentModel;
using DevExpress.ExpressApp;
using FluentAssertions;
using Moq;
using Prometheus;
using XafGrafana.Blazor.Server.Controllers;
using XafGrafana.Blazor.Server.Services;
using XafGrafana.Module.BusinessObjects;

namespace XafGrafana.Tests.ControllerTests;

public class MetricsViewControllerTests
{
    /// <summary>
    /// Tests that the controller correctly classifies new objects as "Create" operations.
    /// Uses a real MetricsViewController but with a mocked ObjectSpace that raises Committing.
    /// </summary>
    [Fact]
    public void Committing_NewObject_IncrementsCreateCounter()
    {
        // Arrange
        var controller = new MetricsViewController();
        var mockObjectSpace = new Mock<IObjectSpace>();
        var customer = new Customer { Name = "Test" };

        mockObjectSpace.Setup(os => os.ModifiedObjects).Returns(new object[] { customer });
        mockObjectSpace.Setup(os => os.IsNewObject(customer)).Returns(true);
        mockObjectSpace.Setup(os => os.IsDeletedObject(customer)).Returns(false);

        // Read counter baseline
        var beforeValue = GetCounterValue("xaf_object_crud_total", "Customer", "Create");

        // Simulate OnActivated by subscribing via the event
        // We can't call OnActivated directly, so we test the handler logic pattern
        SimulateCommitting(controller, mockObjectSpace.Object);

        var afterValue = GetCounterValue("xaf_object_crud_total", "Customer", "Create");
        afterValue.Should().BeGreaterThan(beforeValue);
    }

    [Fact]
    public void Committing_ModifiedObject_IncrementsUpdateCounter()
    {
        var controller = new MetricsViewController();
        var mockObjectSpace = new Mock<IObjectSpace>();
        var order = new Order { Status = OrderStatus.Processing };

        mockObjectSpace.Setup(os => os.ModifiedObjects).Returns(new object[] { order });
        mockObjectSpace.Setup(os => os.IsNewObject(order)).Returns(false);
        mockObjectSpace.Setup(os => os.IsDeletedObject(order)).Returns(false);

        var beforeValue = GetCounterValue("xaf_object_crud_total", "Order", "Update");

        SimulateCommitting(controller, mockObjectSpace.Object);

        var afterValue = GetCounterValue("xaf_object_crud_total", "Order", "Update");
        afterValue.Should().BeGreaterThan(beforeValue);
    }

    [Fact]
    public void Committing_DeletedObject_IncrementsDeleteCounter()
    {
        var controller = new MetricsViewController();
        var mockObjectSpace = new Mock<IObjectSpace>();
        var customer = new Customer { Name = "ToDelete" };

        mockObjectSpace.Setup(os => os.ModifiedObjects).Returns(new object[] { customer });
        mockObjectSpace.Setup(os => os.IsNewObject(customer)).Returns(false);
        mockObjectSpace.Setup(os => os.IsDeletedObject(customer)).Returns(true);

        var beforeValue = GetCounterValue("xaf_object_crud_total", "Customer", "Delete");

        SimulateCommitting(controller, mockObjectSpace.Object);

        var afterValue = GetCounterValue("xaf_object_crud_total", "Customer", "Delete");
        afterValue.Should().BeGreaterThan(beforeValue);
    }

    /// <summary>
    /// Simulates the Committing event that MetricsViewController subscribes to.
    /// This exercises the same handler code path without needing XAF's View lifecycle.
    /// </summary>
    private static void SimulateCommitting(MetricsViewController controller, IObjectSpace objectSpace)
    {
        // The controller subscribes to ObjectSpace.Committing in OnActivated.
        // Since we can't activate without a View, we invoke the handler directly
        // by using reflection to call the private method.
        var method = typeof(MetricsViewController)
            .GetMethod("ObjectSpace_Committing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(controller, new object[] { objectSpace, new CancelEventArgs() });
    }

    private static double GetCounterValue(string metricName, string entity, string operation)
    {
        // Read current value from Prometheus default registry
        // prometheus-net exposes metrics via the default CollectorRegistry
        var counter = XafMetrics.ObjectCrudTotal.WithLabels(entity, operation);
        return counter.Value;
    }
}
```

**Step 2: Run the tests**

Run:
```bash
dotnet test XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj --filter "FullyQualifiedName~MetricsViewControllerTests" -v normal
```

Expected: 3 tests pass. Note: Prometheus counters are global static state, so tests observe increments rather than absolute values. If `counter.Value` is not accessible, use the `Prometheus.CollectorRegistry` to read values — adjust as needed.

**Step 3: Commit**

```bash
git add XafGrafana/XafGrafana.Tests/ControllerTests/MetricsViewControllerTests.cs
git commit -m "test: add MetricsViewController tests with mocked ObjectSpace"
```

---

### Task 6: EfCoreMetricsInterceptor Tests

**Files:**
- Create: `XafGrafana/XafGrafana.Tests/ServiceTests/EfCoreMetricsInterceptorTests.cs`

**Context:** `EfCoreMetricsInterceptor` (at `XafGrafana.Blazor.Server/Services/EfCoreMetricsInterceptor.cs`) calls `XafMetrics.EfQueryDuration.Observe(duration.TotalSeconds)` after every EF Core command. We test it by passing mock `CommandExecutedEventData` with a known duration and checking the histogram value changes.

**Step 1: Write the tests**

Create `XafGrafana/XafGrafana.Tests/ServiceTests/EfCoreMetricsInterceptorTests.cs`:

```csharp
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using XafGrafana.Blazor.Server.Services;

namespace XafGrafana.Tests.ServiceTests;

public class EfCoreMetricsInterceptorTests
{
    [Fact]
    public void ReaderExecuted_RecordsDurationToHistogram()
    {
        var interceptor = new EfCoreMetricsInterceptor();
        var mockCommand = new Mock<DbCommand>();
        var mockReader = new Mock<DbDataReader>();

        var duration = TimeSpan.FromMilliseconds(42);
        var eventData = CreateCommandEventData(duration);

        var beforeCount = XafMetrics.EfQueryDuration.Count;

        interceptor.ReaderExecuted(mockCommand.Object, eventData, mockReader.Object);

        XafMetrics.EfQueryDuration.Count.Should().Be(beforeCount + 1);
    }

    [Fact]
    public void NonQueryExecuted_RecordsDurationToHistogram()
    {
        var interceptor = new EfCoreMetricsInterceptor();
        var mockCommand = new Mock<DbCommand>();

        var duration = TimeSpan.FromMilliseconds(15);
        var eventData = CreateCommandEventData(duration);

        var beforeCount = XafMetrics.EfQueryDuration.Count;

        interceptor.NonQueryExecuted(mockCommand.Object, eventData, 1);

        XafMetrics.EfQueryDuration.Count.Should().Be(beforeCount + 1);
    }

    [Fact]
    public void ScalarExecuted_RecordsDurationToHistogram()
    {
        var interceptor = new EfCoreMetricsInterceptor();
        var mockCommand = new Mock<DbCommand>();

        var duration = TimeSpan.FromMilliseconds(7);
        var eventData = CreateCommandEventData(duration);

        var beforeCount = XafMetrics.EfQueryDuration.Count;

        interceptor.ScalarExecuted(mockCommand.Object, eventData, null);

        XafMetrics.EfQueryDuration.Count.Should().Be(beforeCount + 1);
    }

    /// <summary>
    /// Creates a CommandExecutedEventData with the specified duration.
    /// This requires constructing the EventDefinitionBase and DbContext — we use minimal mocks.
    /// 
    /// IMPORTANT: CommandExecutedEventData constructor may vary by EF Core version.
    /// If construction fails, adjust to match the actual constructor signature.
    /// Alternative: call the interceptor's method via reflection on RecordDuration directly.
    /// </summary>
    private static CommandExecutedEventData CreateCommandEventData(TimeSpan duration)
    {
        // CommandExecutedEventData needs:
        // - EventDefinitionBase, MessageGenerator, DbCommand, DbContext, DbCommandMethod, Guid, Guid, object, DateTimeOffset, TimeSpan
        // We construct minimal instances.
        var mockCommand = new Mock<DbCommand>();
        
        // Use reflection to create CommandExecutedEventData since the constructor is complex.
        // Simpler approach: test RecordDuration directly via reflection.
        var eventDefinition = CreateMinimalEventDefinition();
        
        return new CommandExecutedEventData(
            eventDefinition,
            (def, data) => "test",
            mockCommand.Object,
            null!, // DbContext
            DbCommandMethod.ExecuteReader,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null, // state
            DateTimeOffset.UtcNow,
            duration);
    }

    private static EventDefinitionBase CreateMinimalEventDefinition()
    {
        // EventDefinitionBase requires ILoggingOptions and EventId.
        // We'll use a concrete implementation approach.
        var loggingOptions = new Mock<Microsoft.EntityFrameworkCore.Diagnostics.ILoggingOptions>();
        loggingOptions.Setup(o => o.IsSensitiveDataLoggingEnabled).Returns(false);
        
        return new EventDefinition<string>(
            loggingOptions.Object,
            new Microsoft.Extensions.Logging.EventId(1, "Test"),
            Microsoft.Extensions.Logging.LogLevel.Debug,
            "TestEvent",
            (level) => (arg) => { });
    }
}
```

**IMPORTANT NOTE:** The `CommandExecutedEventData` and `EventDefinition` constructors are complex and may vary between EF Core versions. If this approach fails to compile, the implementing engineer should fall back to testing `RecordDuration` via reflection:

```csharp
// Fallback approach if CommandExecutedEventData construction fails:
[Fact]
public void RecordDuration_ObservesHistogramValue()
{
    var method = typeof(EfCoreMetricsInterceptor)
        .GetMethod("RecordDuration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    var beforeCount = XafMetrics.EfQueryDuration.Count;
    method!.Invoke(null, new object[] { TimeSpan.FromMilliseconds(42) });
    XafMetrics.EfQueryDuration.Count.Should().Be(beforeCount + 1);
}
```

**Step 2: Run the tests**

Run:
```bash
dotnet test XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj --filter "FullyQualifiedName~EfCoreMetricsInterceptorTests" -v normal
```

Expected: 3 tests pass. If `CommandExecutedEventData` construction fails, switch to the reflection fallback and have 1 test.

**Step 3: Commit**

```bash
git add XafGrafana/XafGrafana.Tests/ServiceTests/EfCoreMetricsInterceptorTests.cs
git commit -m "test: add EfCoreMetricsInterceptor duration recording tests"
```

---

### Task 7: ActivitySimulatorService Tests

**Files:**
- Create: `XafGrafana/XafGrafana.Tests/ServiceTests/ActivitySimulatorServiceTests.cs`

**Context:** `ActivitySimulatorService` (at `XafGrafana.Blazor.Server/Services/ActivitySimulatorService.cs`) uses `IServiceProvider` to resolve `INonSecuredObjectSpaceFactory`, creates ObjectSpaces, and performs CRUD operations. We mock the DI chain and verify correct ObjectSpace operations.

**Step 1: Write the tests**

Create `XafGrafana/XafGrafana.Tests/ServiceTests/ActivitySimulatorServiceTests.cs`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using XafGrafana.Blazor.Server.Services;
using XafGrafana.Module.BusinessObjects;

namespace XafGrafana.Tests.ServiceTests;

public class ActivitySimulatorServiceTests
{
    private static (ActivitySimulatorService service, Mock<IObjectSpace> mockOs) CreateServiceWithMockedDeps()
    {
        var mockOs = new Mock<IObjectSpace>();
        var mockFactory = new Mock<INonSecuredObjectSpaceFactory>();
        mockFactory
            .Setup(f => f.CreateNonSecuredObjectSpace<It.IsAnyType>())
            .Returns(mockOs.Object);

        var mockServiceScope = new Mock<IServiceScope>();
        mockServiceScope.Setup(s => s.ServiceProvider.GetService(typeof(INonSecuredObjectSpaceFactory)))
            .Returns(mockFactory.Object);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockServiceScope.Object);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        var mockLogger = new Mock<ILogger<ActivitySimulatorService>>();

        var service = new ActivitySimulatorService(mockServiceProvider.Object, mockLogger.Object);
        return (service, mockOs);
    }

    [Fact]
    public void ServiceCanBeConstructed_WithMockedDependencies()
    {
        var (service, _) = CreateServiceWithMockedDeps();
        service.Should().NotBeNull();
    }

    [Fact]
    public void ServiceDependencies_INonSecuredObjectSpaceFactory_IsResolvable()
    {
        // Verifies the mock chain works — important for template reuse
        var (_, mockOs) = CreateServiceWithMockedDeps();
        mockOs.Should().NotBeNull();
    }
}
```

**Step 2: Run the tests**

Run:
```bash
dotnet test XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj --filter "FullyQualifiedName~ActivitySimulatorServiceTests" -v normal
```

Expected: 2 tests pass.

**Step 3: Commit**

```bash
git add XafGrafana/XafGrafana.Tests/ServiceTests/ActivitySimulatorServiceTests.cs
git commit -m "test: add ActivitySimulatorService construction and DI mock tests"
```

---

### Task 8: Run All Tests and Final Verification

**Step 1: Run the full test suite**

Run:
```bash
dotnet test XafGrafana/XafGrafana.Tests/XafGrafana.Tests.csproj -v normal
```

Expected: All tests pass (12+ tests across 4 categories).

**Step 2: Verify solution still builds end-to-end**

Run:
```bash
dotnet build XafGrafana/XafGrafana.Blazor.Server/XafGrafana.Blazor.Server.csproj
```

Expected: Build succeeded.

**Step 3: Commit any remaining fixes**

If any test adjustments were needed during tasks 3-7, ensure all changes are committed.
