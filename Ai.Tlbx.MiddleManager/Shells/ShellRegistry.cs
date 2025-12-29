using Ai.Tlbx.MiddleManager.Shells;

namespace Ai.Tlbx.MiddleManager.Shells
{
    public sealed class ShellRegistry
    {
        private readonly Dictionary<ShellType, IShellConfiguration> _shells;

        public ShellRegistry()
        {
            _shells = new Dictionary<ShellType, IShellConfiguration>
            {
                [ShellType.Pwsh] = new PwshShellConfiguration(),
                [ShellType.PowerShell] = new PowerShellShellConfiguration(),
                [ShellType.Cmd] = new CmdShellConfiguration(),
                [ShellType.Bash] = new BashShellConfiguration(),
                [ShellType.Zsh] = new ZshShellConfiguration()
            };
        }

        public IShellConfiguration GetConfiguration(ShellType shellType)
        {
            return _shells.TryGetValue(shellType, out var config)
                ? config
                : throw new ArgumentException($"Unknown shell type: {shellType}");
        }

        public IEnumerable<IShellConfiguration> GetAllShells()
        {
            return _shells.Values;
        }

        public IEnumerable<IShellConfiguration> GetAvailableShells()
        {
            return _shells.Values.Where(s => s.IsAvailable());
        }

        public ShellType GetDefaultShell()
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (_shells[ShellType.Zsh].IsAvailable())
                {
                    return ShellType.Zsh;
                }

                if (_shells[ShellType.Bash].IsAvailable())
                {
                    return ShellType.Bash;
                }
            }

            if (_shells[ShellType.Pwsh].IsAvailable())
            {
                return ShellType.Pwsh;
            }

            if (_shells[ShellType.PowerShell].IsAvailable())
            {
                return ShellType.PowerShell;
            }

            if (OperatingSystem.IsWindows())
            {
                return ShellType.Cmd;
            }

            throw new InvalidOperationException("No shell available");
        }

        public IShellConfiguration GetConfigurationOrDefault(ShellType? preferred)
        {
            if (preferred.HasValue && _shells.TryGetValue(preferred.Value, out var config) && config.IsAvailable())
            {
                return config;
            }

            return GetConfiguration(GetDefaultShell());
        }
    }
}
