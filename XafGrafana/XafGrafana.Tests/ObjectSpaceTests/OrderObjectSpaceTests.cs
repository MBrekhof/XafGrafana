using DevExpress.ExpressApp;
using FluentAssertions;
using Xunit;
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
