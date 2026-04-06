namespace XafGrafana.Blazor.Server.Services;

public enum StressTestType
{
    None,
    LoadTest,
    ChaosTest
}

public enum StressTestPreset
{
    Light,
    Medium,
    Heavy
}

public static class StressTestManager
{
    private static readonly object _lock = new();
    private static long _operationCount;

    public static StressTestType ActiveTest { get; private set; } = StressTestType.None;
    public static StressTestPreset ActivePreset { get; private set; } = StressTestPreset.Light;
    public static DateTime? StartedAt { get; private set; }
    public static long OperationCount => Interlocked.Read(ref _operationCount);

    public static bool TryStart(StressTestType testType, StressTestPreset preset)
    {
        lock (_lock)
        {
            if (ActiveTest != StressTestType.None)
                return false;

            ActiveTest = testType;
            ActivePreset = preset;
            StartedAt = DateTime.Now;
            Interlocked.Exchange(ref _operationCount, 0);
            return true;
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            ActiveTest = StressTestType.None;
            StartedAt = null;
            Interlocked.Exchange(ref _operationCount, 0);
        }
    }

    public static void IncrementOps(int count = 1)
    {
        Interlocked.Add(ref _operationCount, count);
    }

    public static bool IsRunning(StressTestType testType) => ActiveTest == testType;
}
