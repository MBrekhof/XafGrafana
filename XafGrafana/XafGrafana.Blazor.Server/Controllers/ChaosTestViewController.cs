using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using XafGrafana.Blazor.Server.BusinessObjects;
using XafGrafana.Blazor.Server.Services;

namespace XafGrafana.Blazor.Server.Controllers;

public class ChaosTestViewController : ObjectViewController<DetailView, ChaosTestParameters>
{
    private readonly SimpleAction _startAction;
    private readonly SimpleAction _stopAction;

    public ChaosTestViewController()
    {
        _startAction = new SimpleAction(this, "StartChaosTest", PredefinedCategory.Edit)
        {
            Caption = "Start",
            ImageName = "Action_Debug_Start",
            ConfirmationMessage = null
        };
        _startAction.Execute += StartAction_Execute;

        _stopAction = new SimpleAction(this, "StopChaosTest", PredefinedCategory.Edit)
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
        var parameters = (ChaosTestParameters)View.CurrentObject;
        if (!StressTestManager.TryStart(StressTestType.ChaosTest, parameters.Preset))
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
        var isRunning = StressTestManager.IsRunning(StressTestType.ChaosTest);
        _startAction.Enabled["NotRunning"] = !isRunning;
        _stopAction.Enabled["IsRunning"] = isRunning;
    }
}
