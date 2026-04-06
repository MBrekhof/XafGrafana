using System.Collections;
using DevExpress.ExpressApp;
using XafGrafana.Blazor.Server.BusinessObjects;

namespace XafGrafana.Blazor.Server.Controllers;

/// <summary>
/// Handles NonPersistentObjectSpace events for stress test parameter objects.
/// Provides singleton instances for ListView display and ensures proper
/// ObjectSpace ownership when opening DetailViews.
/// </summary>
public class StressTestObjectSpaceController : WindowController
{
    private readonly Dictionary<Type, object> _instances = new();

    protected override void OnActivated()
    {
        base.OnActivated();
        Application.ObjectSpaceCreated += Application_ObjectSpaceCreated;
    }

    protected override void OnDeactivated()
    {
        Application.ObjectSpaceCreated -= Application_ObjectSpaceCreated;
        _instances.Clear();
        base.OnDeactivated();
    }

    private void Application_ObjectSpaceCreated(object sender, ObjectSpaceCreatedEventArgs e)
    {
        if (e.ObjectSpace is not NonPersistentObjectSpace npOs) return;

        npOs.ObjectsGetting += NpOs_ObjectsGetting;
        npOs.ObjectByKeyGetting += NpOs_ObjectByKeyGetting;
    }

    private void NpOs_ObjectsGetting(object sender, ObjectsGettingEventArgs e)
    {
        if (e.ObjectType == typeof(LoadTestParameters))
        {
            var os = (NonPersistentObjectSpace)sender;
            var instance = GetOrCreateInstance<LoadTestParameters>(os);
            e.Objects = new ArrayList { instance };
        }
        else if (e.ObjectType == typeof(ChaosTestParameters))
        {
            var os = (NonPersistentObjectSpace)sender;
            var instance = GetOrCreateInstance<ChaosTestParameters>(os);
            e.Objects = new ArrayList { instance };
        }
    }

    private void NpOs_ObjectByKeyGetting(object sender, ObjectByKeyGettingEventArgs e)
    {
        if (e.ObjectType == typeof(LoadTestParameters) || e.ObjectType == typeof(ChaosTestParameters))
        {
            var os = (NonPersistentObjectSpace)sender;
            if (e.ObjectType == typeof(LoadTestParameters))
                e.Object = GetOrCreateInstance<LoadTestParameters>(os);
            else
                e.Object = GetOrCreateInstance<ChaosTestParameters>(os);
        }
    }

    private T GetOrCreateInstance<T>(NonPersistentObjectSpace os) where T : class
    {
        if (_instances.TryGetValue(typeof(T), out var existing))
        {
            // Re-create in current ObjectSpace to avoid cross-ObjectSpace issues
            var fresh = os.CreateObject<T>();
            CopyProperties(existing, fresh);
            return fresh;
        }

        var instance = os.CreateObject<T>();
        _instances[typeof(T)] = instance;
        return instance;
    }

    private static void CopyProperties(object source, object target)
    {
        if (source is LoadTestParameters src1 && target is LoadTestParameters tgt1)
        {
            tgt1.Preset = src1.Preset;
        }
        else if (source is ChaosTestParameters src2 && target is ChaosTestParameters tgt2)
        {
            tgt2.Preset = src2.Preset;
        }
    }
}
