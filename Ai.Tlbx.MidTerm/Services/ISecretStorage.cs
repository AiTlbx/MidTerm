namespace Ai.Tlbx.MidTerm.Services;

public interface ISecretStorage
{
    string? GetSecret(string key);
    void SetSecret(string key, string value);
    void DeleteSecret(string key);
}

public static class SecretKeys
{
    public const string SessionSecret = "midterm.session_secret";
    public const string PasswordHash = "midterm.password_hash";
    public const string CertificatePassword = "midterm.certificate_password";
}
