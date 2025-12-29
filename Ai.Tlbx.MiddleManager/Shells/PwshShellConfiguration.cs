namespace Ai.Tlbx.MiddleManager.Shells
{
    public sealed class PwshShellConfiguration : ShellConfigurationBase
    {
        private const string Osc7PromptScript =
            "function prompt{$e=[char]27;$b=[char]7;$p='/'+($PWD.Path-replace'\\\\','/');\"$e]7;file://$env:COMPUTERNAME$p$b\"+\"PS $PWD> \"}";

        public override ShellType ShellType => ShellType.Pwsh;
        public override string DisplayName => "PowerShell 7";
        public override string ExecutablePath => "pwsh";
        public override bool SupportsOsc7 => true;

        public override string[] Arguments => ["-NoLogo", "-NoExit", "-Command", Osc7PromptScript];
    }
}
