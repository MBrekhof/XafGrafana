using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using XafGrafana.Module.BusinessObjects;

namespace XafGrafana.Blazor.Server.Services;

public class LoadTestService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LoadTestService> _logger;
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

    public LoadTestService(IServiceProvider serviceProvider, ILogger<LoadTestService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!StressTestManager.IsRunning(StressTestType.LoadTest))
            {
                await Task.Delay(500, stoppingToken);
                continue;
            }

            try
            {
                var preset = StressTestManager.ActivePreset;
                var batchSize = preset == StressTestPreset.Heavy ? 10 : 1;

                for (int i = 0; i < batchSize; i++)
                {
                    var action = _random.Next(100);

                    if (action < 40)
                        CreateCustomer();
                    else if (action < 70)
                        CreateOrder();
                    else if (action < 90)
                        UpdateOrderStatus();
                    else
                        DeleteCancelledOrder();

                    StressTestManager.IncrementOps();
                }

                var delayMs = preset switch
                {
                    StressTestPreset.Light => 200,   // ~5 ops/sec
                    StressTestPreset.Medium => 50,    // ~20 ops/sec
                    StressTestPreset.Heavy => 20,     // ~50 ops/sec (10 per batch)
                    _ => 200
                };

                await Task.Delay(delayMs, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Load test action failed");
                XafMetrics.ErrorsTotal.WithLabels("load_test").Inc();
                await Task.Delay(100, stoppingToken);
            }
        }
    }

    private void CreateCustomer()
    {
        using var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var os = factory.CreateNonSecuredObjectSpace<Customer>();

        var customer = os.CreateObject<Customer>();
        customer.Name = $"{FirstNames[_random.Next(FirstNames.Length)]} {LastNames[_random.Next(LastNames.Length)]}";
        customer.Email = $"loadtest.{_random.Next(100000)}@example.com";
        customer.City = Cities[_random.Next(Cities.Length)];
        os.CommitChanges();

        XafMetrics.ObjectCrudTotal.WithLabels("Customer", "Create").Inc();
    }

    private void CreateOrder()
    {
        using var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var os = factory.CreateNonSecuredObjectSpace<Order>();

        var customers = os.GetObjects<Customer>();
        if (!customers.Any()) return;

        var customer = customers.ElementAt(_random.Next(customers.Count));
        var order = os.CreateObject<Order>();
        order.Customer = customer;
        order.OrderDate = DateTime.Now;
        order.Amount = Math.Round((decimal)(_random.NextDouble() * 500 + 10), 2);
        order.Status = OrderStatus.New;
        os.CommitChanges();

        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Create").Inc();
    }

    private void UpdateOrderStatus()
    {
        using var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var os = factory.CreateNonSecuredObjectSpace<Order>();

        var orders = os.GetObjects<Order>()
            .Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)
            .ToList();

        if (!orders.Any()) return;

        var order = orders[_random.Next(orders.Count)];
        order.Status = _random.Next(10) < 2 ? OrderStatus.Cancelled : order.Status + 1;
        os.CommitChanges();

        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Update").Inc();
    }

    private void DeleteCancelledOrder()
    {
        using var scope = _serviceProvider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var os = factory.CreateNonSecuredObjectSpace<Order>();

        var cancelled = os.GetObjects<Order>()
            .Where(o => o.Status == OrderStatus.Cancelled)
            .ToList();

        if (!cancelled.Any()) return;

        var order = cancelled[_random.Next(cancelled.Count)];
        os.Delete(order);
        os.CommitChanges();

        XafMetrics.ObjectCrudTotal.WithLabels("Order", "Delete").Inc();
    }
}
