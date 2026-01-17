using Ai.Tlbx.MidTerm.Services;
using Ai.Tlbx.MidTerm.Settings;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Ai.Tlbx.MidTerm.UnitTests;

public class AuthServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settingsService;
    private readonly FakeTimeProvider _timeProvider;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"midterm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settingsService = new SettingsService(_tempDir);
        _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        _authService = new AuthService(_settingsService, _timeProvider);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void HashPassword_VerifyPassword_RoundTrip()
    {
        var password = "MySecurePassword123!";

        var hash = _authService.HashPassword(password);
        var result = _authService.VerifyPassword(password, hash);

        Assert.True(result);
    }

    [Fact]
    public void HashPassword_ProducesDifferentHashesForSamePassword()
    {
        var password = "TestPassword";

        var hash1 = _authService.HashPassword(password);
        var hash2 = _authService.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var hash = _authService.HashPassword("CorrectPassword");

        var result = _authService.VerifyPassword("WrongPassword", hash);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_CorruptedHash_ReturnsFalse()
    {
        var corruptedHash = "$PBKDF2$100000$invalidbase64$alsonotvalid";

        var result = _authService.VerifyPassword("AnyPassword", corruptedHash);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_MalformedHash_ReturnsFalse()
    {
        var malformedHash = "notavalidhash";

        var result = _authService.VerifyPassword("AnyPassword", malformedHash);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_EmptyPassword_ReturnsFalse()
    {
        var hash = _authService.HashPassword("SomePassword");

        var result = _authService.VerifyPassword("", hash);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_NullHash_ReturnsFalse()
    {
        var result = _authService.VerifyPassword("AnyPassword", null);

        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_EmptyHash_ReturnsFalse()
    {
        var result = _authService.VerifyPassword("AnyPassword", "");

        Assert.False(result);
    }

    [Fact]
    public void RateLimit_FourFailures_NotLocked()
    {
        var ip = "192.168.1.1";

        for (var i = 0; i < 4; i++)
        {
            _authService.RecordFailedAttempt(ip);
        }

        Assert.False(_authService.IsRateLimited(ip));
    }

    [Fact]
    public void RateLimit_FiveFailures_30SecondLockout()
    {
        var ip = "192.168.1.2";

        for (var i = 0; i < 5; i++)
        {
            _authService.RecordFailedAttempt(ip);
        }

        Assert.True(_authService.IsRateLimited(ip));

        var remaining = _authService.GetRemainingLockout(ip);
        Assert.NotNull(remaining);
        Assert.True(remaining.Value.TotalSeconds <= 30);
        Assert.True(remaining.Value.TotalSeconds > 0);
    }

    [Fact]
    public void RateLimit_TenFailures_5MinuteLockout()
    {
        var ip = "192.168.1.3";

        for (var i = 0; i < 10; i++)
        {
            _authService.RecordFailedAttempt(ip);
        }

        Assert.True(_authService.IsRateLimited(ip));

        var remaining = _authService.GetRemainingLockout(ip);
        Assert.NotNull(remaining);
        Assert.True(remaining.Value.TotalMinutes <= 5);
        Assert.True(remaining.Value.TotalMinutes > 0.5);
    }

    [Fact]
    public void RateLimit_ResetAttempts_ClearsLockout()
    {
        var ip = "192.168.1.4";

        for (var i = 0; i < 5; i++)
        {
            _authService.RecordFailedAttempt(ip);
        }
        Assert.True(_authService.IsRateLimited(ip));

        _authService.ResetAttempts(ip);

        Assert.False(_authService.IsRateLimited(ip));
        Assert.Null(_authService.GetRemainingLockout(ip));
    }

    [Fact]
    public void RateLimit_LockoutExpires_AfterTime()
    {
        var ip = "192.168.1.5";

        for (var i = 0; i < 5; i++)
        {
            _authService.RecordFailedAttempt(ip);
        }
        Assert.True(_authService.IsRateLimited(ip));

        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        Assert.False(_authService.IsRateLimited(ip));
    }

    [Fact]
    public void RateLimit_5MinLockoutExpires_AfterTime()
    {
        var ip = "192.168.1.6";

        for (var i = 0; i < 10; i++)
        {
            _authService.RecordFailedAttempt(ip);
        }
        Assert.True(_authService.IsRateLimited(ip));

        _timeProvider.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        Assert.False(_authService.IsRateLimited(ip));
    }

    [Fact]
    public void SessionToken_ValidWithin72Hours()
    {
        var token = _authService.CreateSessionToken();

        _timeProvider.Advance(TimeSpan.FromHours(71));

        Assert.True(_authService.ValidateSessionToken(token));
    }

    [Fact]
    public void SessionToken_InvalidAfter72Hours()
    {
        var token = _authService.CreateSessionToken();

        _timeProvider.Advance(TimeSpan.FromHours(73));

        Assert.False(_authService.ValidateSessionToken(token));
    }

    [Fact]
    public void SessionToken_TamperedSignature_Invalid()
    {
        var token = _authService.CreateSessionToken();
        var parts = token.Split(':');
        var tamperedToken = $"{parts[0]}:tampered_signature";

        Assert.False(_authService.ValidateSessionToken(tamperedToken));
    }

    [Fact]
    public void SessionToken_TamperedTimestamp_Invalid()
    {
        var token = _authService.CreateSessionToken();
        var parts = token.Split(':');
        var tamperedToken = $"9999999999:{parts[1]}";

        Assert.False(_authService.ValidateSessionToken(tamperedToken));
    }

    [Fact]
    public void SessionToken_NullToken_Invalid()
    {
        Assert.False(_authService.ValidateSessionToken(null));
    }

    [Fact]
    public void SessionToken_EmptyToken_Invalid()
    {
        Assert.False(_authService.ValidateSessionToken(""));
    }

    [Fact]
    public void SessionToken_MalformedToken_Invalid()
    {
        Assert.False(_authService.ValidateSessionToken("notavalidtoken"));
    }
}
