using System.Diagnostics.CodeAnalysis;

namespace Ai.Tlbx.MidTerm.Services;

public static class SecretStorageFactory
{
    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
        Justification = "Platform checks are performed via OperatingSystem.IsX() guards")]
    public static ISecretStorage Create(string settingsDirectory, bool isServiceMode)
    {
#if WINDOWS
        return new WindowsSecretStorage(settingsDirectory, isServiceMode);
#else
        if (OperatingSystem.IsMacOS())
        {
            return new MacOsSecretStorage();
        }

        return new LinuxSecretStorage(settingsDirectory);
#endif
    }
}
