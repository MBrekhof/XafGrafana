using DevExpress.ExpressApp;
using DevExpress.ExpressApp.EFCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using XafGrafana.Module.BusinessObjects;

namespace XafGrafana.Tests.Infrastructure;

/// <summary>
/// Overrides the global change tracking strategy to Snapshot so that
/// the InMemory provider does not reject DevExpress entities (e.g. DashboardData)
/// that don't implement INotifyPropertyChanged.
/// </summary>
internal sealed class SnapshotChangeTrackingCustomizer : ModelCustomizer
{
    public SnapshotChangeTrackingCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.Snapshot);
    }
}

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
                builder.ReplaceService<IModelCustomizer, SnapshotChangeTrackingCustomizer>();
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
