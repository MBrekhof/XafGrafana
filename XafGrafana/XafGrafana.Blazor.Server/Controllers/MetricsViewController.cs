using DevExpress.ExpressApp;
using XafGrafana.Blazor.Server.Services;

namespace XafGrafana.Blazor.Server.Controllers;

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

            if (objectSpace.IsNewObject(obj))
                XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Create").Inc();
            else if (objectSpace.IsDeletedObject(obj))
                XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Delete").Inc();
            else
                XafMetrics.ObjectCrudTotal.WithLabels(entityName, "Update").Inc();
        }
    }
}
