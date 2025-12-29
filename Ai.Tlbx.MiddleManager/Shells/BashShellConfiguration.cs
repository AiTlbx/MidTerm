namespace Ai.Tlbx.MiddleManager.Shells;

public sealed class BashShellConfiguration : ShellConfigurationBase
{
    public override ShellType ShellType => ShellType.Bash;
    public override string DisplayName => "Bash";
    public override string ExecutablePath => "bash";
    public override bool SupportsOsc7 => true;

    public override string[] Arguments => ["-l"];

    public override Dictionary<string, string> GetEnvironmentVariables()
    {
        var env = base.GetEnvironmentVariables();
        env["PROMPT_COMMAND"] = "printf '\\e]7;file://%s%s\\a' \"$HOSTNAME\" \"$PWD\"";
        return env;
    }

    public override bool IsAvailable()
    {
        return (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && base.IsAvailable();
    }
}
