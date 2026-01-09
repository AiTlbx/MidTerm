using System.Diagnostics.CodeAnalysis;

namespace Ai.Tlbx.MidTerm.Services;

public static class SecretStorageFactory
{
    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
        Justification = "Platform checks are performed via OperatingSystem.IsX() guards")]
    public static ISecretStorage Create(string settingsDirectory, bool isServiceMode)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSecretStorage(settingsDirectory, isServiceMode);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsSecretStorage();
        }

        // Linux and other Unix-like systems
        return new LinuxSecretStorage(settingsDirectory);
    }
}
