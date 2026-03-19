using DevExpress.ExpressApp;
using XafGraphana.Blazor.Server.Services;

namespace XafGraphana.Blazor.Server.Controllers;

public class MetricsViewController : ViewController
{
    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.Committing += ObjectSpace_Committing;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.Committing -= ObjectSpace_Committing;
        base.OnDeactivated();
    }

    private void ObjectSpace_Committing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not IObjectSpace objectSpace) return;

        foreach (var obj in objectSpace.ModifiedObjects)
        {
            if (obj == null) continue;
            var entityName = obj.GetType().Name;
            var state = objectSpace.GetObjectState(obj);

            switch (state)
            {
                case ObjectState.New:
                    XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Create").Inc();
                    break;
                case ObjectState.Modified:
                    XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Update").Inc();
                    break;
                case ObjectState.Deleted:
                    XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Delete").Inc();
                    break;
            }
        }
    }
}
