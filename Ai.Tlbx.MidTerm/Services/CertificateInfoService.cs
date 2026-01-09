using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Ai.Tlbx.MidTerm.Services;

public sealed class CertificateInfoService
{
    public string? Fingerprint { get; private set; }
    public DateTime? NotBefore { get; private set; }
    public DateTime? NotAfter { get; private set; }
    public bool IsFallbackCertificate { get; private set; }

    public void SetCertificate(X509Certificate2 cert, bool isFallback)
    {
        Fingerprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
        NotBefore = cert.NotBefore.ToUniversalTime();
        NotAfter = cert.NotAfter.ToUniversalTime();
        IsFallbackCertificate = isFallback;
    }

    public CertificateInfoResponse GetInfo()
    {
        return new CertificateInfoResponse
        {
            Fingerprint = Fingerprint,
            NotBefore = NotBefore,
            NotAfter = NotAfter,
            IsFallbackCertificate = IsFallbackCertificate
        };
    }
}

public sealed class CertificateInfoResponse
{
    public string? Fingerprint { get; init; }
    public DateTime? NotBefore { get; init; }
    public DateTime? NotAfter { get; init; }
    public bool IsFallbackCertificate { get; init; }
}
