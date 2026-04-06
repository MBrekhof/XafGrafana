using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using System.ComponentModel;
using XafGrafana.Blazor.Server.Services;

namespace XafGrafana.Blazor.Server.BusinessObjects;

[DomainComponent]
[DefaultClassOptions]
[NavigationItem("Stress Testing")]
[DefaultProperty(nameof(Status))]
[ImageName("Action_Debug_Start")]
public class LoadTestParameters: NonPersistentBaseObject
{
    [VisibleInListView(false)]
    [VisibleInDetailView(true)]
    public StressTestPreset Preset { get; set; } = StressTestPreset.Medium;

    [VisibleInListView(true)]
    [VisibleInDetailView(true)]
    public string Status => StressTestManager.IsRunning(StressTestType.LoadTest)
        ? $"Running ({StressTestManager.ActivePreset}) — {StressTestManager.OperationCount} ops"
        : "Stopped";

    [VisibleInListView(true)]
    [VisibleInDetailView(true)]
    public string ElapsedTime => StressTestManager.StartedAt.HasValue
        ? (DateTime.Now - StressTestManager.StartedAt.Value).ToString(@"hh\:mm\:ss")
        : "—";
}
