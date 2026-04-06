using System.Reflection;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using XafGrafana.Blazor.Server.Services;
using XafGrafana.Module.BusinessObjects;

namespace XafGrafana.Tests.ServiceTests;

public class ActivitySimulatorServiceTests
{
    // INonSecuredObjectSpaceFactory has a single non-generic interface method:
    //   IObjectSpace CreateNonSecuredObjectSpace(Type objectType)
    // The generic CreateNonSecuredObjectSpace<T>() is an extension method that calls it.
    private static (ActivitySimulatorService service, Mock<IObjectSpace> mockOs, Mock<INonSecuredObjectSpaceFactory> mockFactory) CreateServiceWithMockedDeps()
    {
        var mockOs = new Mock<IObjectSpace>();
        var mockFactory = new Mock<INonSecuredObjectSpaceFactory>();
        mockFactory
            .Setup(f => f.CreateNonSecuredObjectSpace(typeof(Customer)))
            .Returns(mockOs.Object);
        mockFactory
            .Setup(f => f.CreateNonSecuredObjectSpace(typeof(Order)))
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
        return (service, mockOs, mockFactory);
    }

    [Fact]
    public void CreateCustomerAsync_CreatesObjectAndCommits()
    {
        var (service, mockOs, _) = CreateServiceWithMockedDeps();
        var customer = new Customer();
        mockOs.Setup(os => os.CreateObject<Customer>()).Returns(customer);

        var method = typeof(ActivitySimulatorService)
            .GetMethod("CreateCustomerAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, null);

        mockOs.Verify(os => os.CreateObject<Customer>(), Times.Once);
        mockOs.Verify(os => os.CommitChanges(), Times.Once);
        customer.Name.Should().NotBeNullOrEmpty();
        customer.Email.Should().NotBeNullOrEmpty();
        customer.City.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateOrderAsync_WithExistingCustomers_CreatesOrderAndCommits()
    {
        var (service, mockOs, _) = CreateServiceWithMockedDeps();
        var customer = new Customer { Name = "Test" };
        var order = new Order();

        mockOs.Setup(os => os.GetObjects<Customer>()).Returns(new List<Customer> { customer });
        mockOs.Setup(os => os.CreateObject<Order>()).Returns(order);

        var method = typeof(ActivitySimulatorService)
            .GetMethod("CreateOrderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, null);

        mockOs.Verify(os => os.CreateObject<Order>(), Times.Once);
        mockOs.Verify(os => os.CommitChanges(), Times.Once);
        order.Customer.Should().Be(customer);
        order.Status.Should().Be(OrderStatus.New);
    }

    [Fact]
    public void CreateOrderAsync_WithNoCustomers_DoesNotCreateOrder()
    {
        var (service, mockOs, _) = CreateServiceWithMockedDeps();
        mockOs.Setup(os => os.GetObjects<Customer>()).Returns(new List<Customer>());

        var method = typeof(ActivitySimulatorService)
            .GetMethod("CreateOrderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, null);

        mockOs.Verify(os => os.CreateObject<Order>(), Times.Never);
        mockOs.Verify(os => os.CommitChanges(), Times.Never);
    }
}
