namespace Ai.Tlbx.MiddleManager.Shells;

public sealed class ZshShellConfiguration : ShellConfigurationBase
{
    public override ShellType ShellType => ShellType.Zsh;
    public override string DisplayName => "Zsh";
    public override string ExecutablePath => "zsh";
    public override bool SupportsOsc7 => true;

    public override string[] Arguments => ["-l"];

    public override bool IsAvailable()
    {
        return (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && base.IsAvailable();
    }
}
