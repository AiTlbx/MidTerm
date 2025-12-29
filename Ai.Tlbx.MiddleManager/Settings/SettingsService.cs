using Ai.Tlbx.MiddleManager.Settings;
using System.Text.Json;

namespace Ai.Tlbx.MiddleManager.Settings
{
    public enum SettingsLoadStatus
    {
        Default,
        LoadedFromFile,
        ErrorFallbackToDefault
    }

    public sealed class SettingsService
    {
        private readonly string _settingsPath;
        private MiddleManagerSettings? _cached;
        private readonly object _lock = new();

        public SettingsLoadStatus LoadStatus { get; private set; } = SettingsLoadStatus.Default;
        public string? LoadError { get; private set; }
        public string SettingsPath => _settingsPath;

        public SettingsService()
        {
            var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Path.Combine(userDir, ".middlemanager");
            _settingsPath = Path.Combine(configDir, "settings.json");
        }

        public MiddleManagerSettings Load()
        {
            lock (_lock)
            {
                if (_cached is not null)
                {
                    return _cached;
                }

                if (!File.Exists(_settingsPath))
                {
                    _cached = new MiddleManagerSettings();
                    LoadStatus = SettingsLoadStatus.Default;
                    return _cached;
                }

                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    _cached = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.MiddleManagerSettings)
                        ?? new MiddleManagerSettings();
                    LoadStatus = SettingsLoadStatus.LoadedFromFile;
                }
                catch (Exception ex)
                {
                    _cached = new MiddleManagerSettings();
                    LoadStatus = SettingsLoadStatus.ErrorFallbackToDefault;
                    LoadError = ex.Message;
                }

                return _cached;
            }
        }

        public void Save(MiddleManagerSettings settings)
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.MiddleManagerSettings);
                File.WriteAllText(_settingsPath, json);
                _cached = settings;
            }
        }

        public void InvalidateCache()
        {
            lock (_lock)
            {
                _cached = null;
            }
        }
    }
}
