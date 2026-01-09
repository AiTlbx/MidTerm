namespace Ai.Tlbx.MidTerm.Services.Security;

public static class CertificateProtectorFactory
{
    public static ICertificateProtector Create(string settingsDirectory, bool isServiceMode)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            return new WindowsDpapiProtector(settingsDirectory, isServiceMode);
        }
#endif
        return new EncryptedFileProtector(settingsDirectory);
    }
}
