using System.Security.Cryptography.X509Certificates;

namespace Ai.Tlbx.MidTerm.Services.Security;

public interface ICertificateProtector
{
    bool IsAvailable { get; }
    void StorePrivateKey(byte[] privateKeyBytes, string keyId);
    byte[] RetrievePrivateKey(string keyId);
    void DeletePrivateKey(string keyId);
    X509Certificate2 LoadCertificateWithPrivateKey(string certificatePath, string keyId);
}
