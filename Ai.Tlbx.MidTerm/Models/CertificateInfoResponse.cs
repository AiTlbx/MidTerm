namespace Ai.Tlbx.MidTerm.Models;

public sealed class CertificateInfoResponse
{
    public string? Fingerprint { get; init; }
    public DateTime? NotBefore { get; init; }
    public DateTime? NotAfter { get; init; }
    public bool IsFallbackCertificate { get; init; }
}
