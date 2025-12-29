namespace Ai.Tlbx.MiddleManager.Shells
{
    public interface IShellConfiguration
    {
        ShellType ShellType { get; }
        string DisplayName { get; }
        string ExecutablePath { get; }
        string[] Arguments { get; }
        bool SupportsOsc7 { get; }

        Dictionary<string, string> GetEnvironmentVariables();
        bool IsAvailable();
    }
}
