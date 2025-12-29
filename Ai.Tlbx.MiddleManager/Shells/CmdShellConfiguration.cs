namespace Ai.Tlbx.MiddleManager.Shells
{
    public sealed class CmdShellConfiguration : ShellConfigurationBase
    {
        public override ShellType ShellType => ShellType.Cmd;
        public override string DisplayName => "Command Prompt";
        public override string ExecutablePath => "cmd.exe";
        public override bool SupportsOsc7 => false;

        public override string[] Arguments => [];

        public override bool IsAvailable()
        {
            return OperatingSystem.IsWindows();
        }
    }
}
