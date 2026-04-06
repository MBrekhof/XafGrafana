using System.ComponentModel;
using DevExpress.ExpressApp;
using FluentAssertions;
using Moq;
using Xunit;
using XafGraphana.Blazor.Server.Controllers;
using XafGraphana.Blazor.Server.Services;

namespace XafGraphana.Tests.ControllerTests;

public class MetricsViewControllerTests
{
    // Plain POCO test doubles whose Type.Name exactly matches the entity name
    // used as Prometheus label values.  We avoid instantiating XAF BaseObject
    // subclasses here because they require an ObjectSpace initialisation context.
    private class Customer
    {
        public string Name { get; set; } = string.Empty;
    }

    private class Order
    {
        public string Status { get; set; } = string.Empty;
    }

    [Fact]
    public void Committing_NewObject_IncrementsCreateCounter()
    {
        var controller = new MetricsViewController();
        var mockObjectSpace = new Mock<IObjectSpace>();
        var customer = new Customer { Name = "Test" };

        mockObjectSpace.Setup(os => os.ModifiedObjects).Returns(new object[] { customer });
        mockObjectSpace.Setup(os => os.IsNewObject(customer)).Returns(true);
        mockObjectSpace.Setup(os => os.IsDeletedObject(customer)).Returns(false);

        var beforeValue = XafMetrics.ObjectCrudTotal.WithLabels("Customer", "Create").Value;
        InvokeCommittingHandler(controller, mockObjectSpace.Object);
        var afterValue = XafMetrics.ObjectCrudTotal.WithLabels("Customer", "Create").Value;

        afterValue.Should().BeGreaterThan(beforeValue);
    }

    [Fact]
    public void Committing_ModifiedObject_IncrementsUpdateCounter()
    {
        var controller = new MetricsViewController();
        var mockObjectSpace = new Mock<IObjectSpace>();
        var order = new Order { Status = "Processing" };

        mockObjectSpace.Setup(os => os.ModifiedObjects).Returns(new object[] { order });
        mockObjectSpace.Setup(os => os.IsNewObject(order)).Returns(false);
        mockObjectSpace.Setup(os => os.IsDeletedObject(order)).Returns(false);

        var beforeValue = XafMetrics.ObjectCrudTotal.WithLabels("Order", "Update").Value;
        InvokeCommittingHandler(controller, mockObjectSpace.Object);
        var afterValue = XafMetrics.ObjectCrudTotal.WithLabels("Order", "Update").Value;

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

        var beforeValue = XafMetrics.ObjectCrudTotal.WithLabels("Customer", "Delete").Value;
        InvokeCommittingHandler(controller, mockObjectSpace.Object);
        var afterValue = XafMetrics.ObjectCrudTotal.WithLabels("Customer", "Delete").Value;

        afterValue.Should().BeGreaterThan(beforeValue);
    }

    private static void InvokeCommittingHandler(MetricsViewController controller, IObjectSpace objectSpace)
    {
        var method = typeof(MetricsViewController)
            .GetMethod("ObjectSpace_Committing",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(controller, new object[] { objectSpace, new CancelEventArgs() });
    }
}
