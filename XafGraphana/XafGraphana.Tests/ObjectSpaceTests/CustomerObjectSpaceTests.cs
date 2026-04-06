using DevExpress.ExpressApp;
using FluentAssertions;
using Xunit;
using XafGraphana.Module.BusinessObjects;
using XafGraphana.Tests.Infrastructure;

namespace XafGraphana.Tests.ObjectSpaceTests;

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
        var loaded = os2.GetObjects<Customer>().FirstOrDefault();
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
        var loaded = os2.GetObjects<Customer>().FirstOrDefault();
        loaded!.Name = "Updated";
        os2.CommitChanges();

        using var os3 = CreateObjectSpace();
        var reloaded = os3.GetObjects<Customer>().FirstOrDefault();
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
        var loaded = os2.GetObjects<Customer>().FirstOrDefault();
        os2.Delete(loaded);
        os2.CommitChanges();

        using var os3 = CreateObjectSpace();
        var result = os3.GetObjects<Customer>().FirstOrDefault();
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
