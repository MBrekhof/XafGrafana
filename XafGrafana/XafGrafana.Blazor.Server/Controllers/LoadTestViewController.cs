using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using XafGrafana.Blazor.Server.BusinessObjects;
using XafGrafana.Blazor.Server.Services;

namespace XafGrafana.Blazor.Server.Controllers;

public class LoadTestViewController : ObjectViewController<DetailView, LoadTestParameters>
{
    private readonly SimpleAction _startAction;
    private readonly SimpleAction _stopAction;

    public LoadTestViewController()
    {
        _startAction = new SimpleAction(this, "StartLoadTest", PredefinedCategory.Edit)
        {
            Caption = "Start",
            ImageName = "Action_Debug_Start",
            ConfirmationMessage = null
        };
        _startAction.Execute += StartAction_Execute;

        _stopAction = new SimpleAction(this, "StopLoadTest", PredefinedCategory.Edit)
        {
            Caption = "Stop",
            ImageName = "Action_Cancel",
            ConfirmationMessage = null
        };
        _stopAction.Execute += StopAction_Execute;
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        UpdateActionState();
    }

    protected override void OnDeactivated()
    {
        _startAction.Execute -= StartAction_Execute;
        _stopAction.Execute -= StopAction_Execute;
        base.OnDeactivated();
    }

    private void StartAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var parameters = (LoadTestParameters)View.CurrentObject;
        if (!StressTestManager.TryStart(StressTestType.LoadTest, parameters.Preset))
        {
            throw new UserFriendlyException(
                $"Cannot start: {StressTestManager.ActiveTest} is already running. Stop it first.");
        }

        UpdateActionState();
        View.Refresh();
    }

    private void StopAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        StressTestManager.Stop();
        UpdateActionState();
        View.Refresh();
    }

    private void UpdateActionState()
    {
        var isRunning = StressTestManager.IsRunning(StressTestType.LoadTest);
        _startAction.Enabled["NotRunning"] = !isRunning;
        _stopAction.Enabled["IsRunning"] = isRunning;
    }
}
