#if WINDOWS
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Ai.Tlbx.MidTerm.Services.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiProtector : ICertificateProtector
{
    private readonly string _keyStorePath;
    private readonly DataProtectionScope _scope;

    public WindowsDpapiProtector(string settingsDirectory, bool isServiceMode)
    {
        _keyStorePath = Path.Combine(settingsDirectory, "keys");
        _scope = isServiceMode ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;
    }

    public bool IsAvailable => OperatingSystem.IsWindows();

    public void StorePrivateKey(byte[] privateKeyBytes, string keyId)
    {
        if (!Directory.Exists(_keyStorePath))
        {
            Directory.CreateDirectory(_keyStorePath);
        }

        var protectedBytes = ProtectedData.Protect(
            privateKeyBytes,
            null,
            _scope);

        var keyPath = GetKeyPath(keyId);
        File.WriteAllBytes(keyPath, protectedBytes);
    }

    public byte[] RetrievePrivateKey(string keyId)
    {
        var keyPath = GetKeyPath(keyId);
        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException($"Protected key not found: {keyId}");
        }

        var protectedBytes = File.ReadAllBytes(keyPath);
        return ProtectedData.Unprotect(
            protectedBytes,
            null,
            _scope);
    }

    public void DeletePrivateKey(string keyId)
    {
        var keyPath = GetKeyPath(keyId);
        if (File.Exists(keyPath))
        {
            File.Delete(keyPath);
        }
    }

    public X509Certificate2 LoadCertificateWithPrivateKey(string certificatePath, string keyId)
    {
        var certPem = File.ReadAllText(certificatePath);
        var cert = X509Certificate2.CreateFromPem(certPem);

        var privateKeyBytes = RetrievePrivateKey(keyId);
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            return cert.CopyWithPrivateKey(ecdsa);
        }
        catch
        {
            // Try RSA if ECDSA fails
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            return cert.CopyWithPrivateKey(rsa);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    private string GetKeyPath(string keyId)
    {
        return Path.Combine(_keyStorePath, $"{keyId}.dpapi");
    }
}
#endif
