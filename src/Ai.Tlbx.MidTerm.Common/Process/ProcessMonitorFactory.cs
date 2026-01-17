namespace Ai.Tlbx.MidTerm.Common.Process;

/// <summary>
/// Factory for creating platform-specific process monitors.
/// </summary>
public static class ProcessMonitorFactory
{
    private static Func<IProcessMonitor>? _factory;

    /// <summary>
    /// Register the platform-specific factory function.
    /// Called at startup by the TtyHost to register the appropriate implementation.
    /// </summary>
    public static void Register(Func<IProcessMonitor> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Create a new process monitor instance.
    /// </summary>
    public static IProcessMonitor Create()
    {
        if (_factory is null)
        {
            throw new InvalidOperationException(
                "ProcessMonitorFactory not initialized. Call Register() at startup.");
        }
        return _factory();
    }
}
