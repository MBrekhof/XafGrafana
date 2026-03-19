using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using XafGraphana.Module.BusinessObjects;

namespace XafGraphana.Blazor.Server.Services;

public class ActivitySimulatorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ActivitySimulatorService> _logger;
    private readonly Random _random = new();

    private static readonly string[] Cities =
        { "Amsterdam", "Rotterdam", "Utrecht", "Den Haag", "Eindhoven",
          "Groningen", "Tilburg", "Almere", "Breda", "Nijmegen" };

    private static readonly string[] FirstNames =
        { "Jan", "Piet", "Klaas", "Marie", "Anna",
          "Sofie", "Daan", "Liam", "Emma", "Lucas" };

    private static readonly string[] LastNames =
        { "de Vries", "Jansen", "van den Berg", "Bakker", "Visser",
          "Smit", "Meijer", "de Boer", "Mulder", "de Groot" };

    public ActivitySimulatorService(
        IServiceProvider serviceProvider,
        ILogger<ActivitySimulatorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the app to fully start and create the database
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        _logger.LogInformation("Activity simulator started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var action = _random.Next(100);

                if (action < 40)
                    await CreateCustomerAsync();
                else if (action < 70)
                    await CreateOrderAsync();
                else if (action < 90)
                    await UpdateOrderStatusAsync();
                else
                    await DeleteCancelledOrderAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Simulator action failed");
                XafMetrics.ErrorsTotal.WithLabels("simulator").Inc();
            }

            // Random delay 3-12 seconds
            var delay = TimeSpan.FromSeconds(3 + _random.Next(10));
            await Task.Delay(delay, stoppingToken);
        }
    }

    private Task CreateCustomerAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var objectSpaceFactory = scope.ServiceProvider
            .GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Customer>();

        var first = FirstNames[_random.Next(FirstNames.Length)];
        var last = LastNames[_random.Next(LastNames.Length)];

        var customer = objectSpace.CreateObject<Customer>();
        customer.Name = $"{first} {last}";
        customer.Email = $"{first.ToLower()}.{last.ToLower().Replace(" ", "")}@example.com";
        customer.City = Cities[_random.Next(Cities.Length)];

        objectSpace.CommitChanges();
        XafMetrics.ObjectCrudTotal.WithLabels("Customer", "Create").Inc();
        _logger.LogDebug("Created customer: {Name}", customer.Name);
        return Task.CompletedTask;
    }

    private Task CreateOrderAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var objectSpaceFactory = scope.ServiceProvider
            .GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Order>();

        var customers = objectSpace.GetObjects<Customer>();
        if (!customers.Any()) return Task.CompletedTask;

        var customer = customers.ElementAt(_random.Next(customers.Count));

        var order = objectSpace.CreateObject<Order>();
        order.Customer = customer;
        order.OrderDate = DateTime.Now;
        order.Amount = Math.Round((decimal)(_random.NextDouble() * 500 + 10), 2);
        order.Status = OrderStatus.New;

        objectSpace.CommitChanges();
        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Create").Inc();
        _logger.LogDebug("Created order for {Customer}, amount: {Amount}", customer.Name, order.Amount);
        return Task.CompletedTask;
    }

    private Task UpdateOrderStatusAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var objectSpaceFactory = scope.ServiceProvider
            .GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Order>();

        var orders = objectSpace.GetObjects<Order>()
            .Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)
            .ToList();

        if (!orders.Any()) return Task.CompletedTask;

        var order = orders[_random.Next(orders.Count)];

        if (_random.Next(10) < 2)
            order.Status = OrderStatus.Cancelled;
        else
            order.Status = order.Status + 1;

        objectSpace.CommitChanges();
        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Update").Inc();
        _logger.LogDebug("Updated order status to {Status}", order.Status);
        return Task.CompletedTask;
    }

    private Task DeleteCancelledOrderAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var objectSpaceFactory = scope.ServiceProvider
            .GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Order>();

        var cancelledOrders = objectSpace.GetObjects<Order>()
            .Where(o => o.Status == OrderStatus.Cancelled)
            .ToList();

        if (!cancelledOrders.Any()) return Task.CompletedTask;

        var order = cancelledOrders[_random.Next(cancelledOrders.Count)];
        objectSpace.Delete(order);
        objectSpace.CommitChanges();
        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Delete").Inc();
        _logger.LogDebug("Deleted cancelled order");
        return Task.CompletedTask;
    }
}
