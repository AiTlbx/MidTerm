namespace Ai.Tlbx.MiddleManager.Shells
{
    public abstract class ShellConfigurationBase : IShellConfiguration
    {
        public abstract ShellType ShellType { get; }
        public abstract string DisplayName { get; }
        public abstract string ExecutablePath { get; }
        public abstract string[] Arguments { get; }
        public abstract bool SupportsOsc7 { get; }

        public virtual Dictionary<string, string> GetEnvironmentVariables()
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key && !string.IsNullOrEmpty(key))
                {
                    env[key] = entry.Value?.ToString() ?? string.Empty;
                }
            }
            env["TERM"] = "xterm-256color";
            return env;
        }

        public virtual bool IsAvailable()
        {
            var resolved = ResolveExecutablePath();
            return resolved is not null;
        }

        protected string? ResolveExecutablePath()
        {
            if (Path.IsPathRooted(ExecutablePath) && File.Exists(ExecutablePath))
            {
                return ExecutablePath;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv is null)
            {
                return null;
            }

            var extensions = OperatingSystem.IsWindows()
                ? new[] { ".exe", ".cmd", ".bat", "" }
                : new[] { "" };

            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                foreach (var ext in extensions)
                {
                    var candidate = ExecutablePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                        ? Path.Combine(dir, ExecutablePath)
                        : Path.Combine(dir, ExecutablePath + ext);

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
    }
}
