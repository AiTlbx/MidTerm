using Ai.Tlbx.MiddleManager.Settings;
using Ai.Tlbx.MiddleManager.Shells;
using Ai.Tlbx.MiddleManager.Services;
using Xunit;

namespace Ai.Tlbx.MiddleManager.Tests;

public class StateListenerTests : IDisposable
{
    private readonly SessionManager _sessionManager = new(new ShellRegistry(), new SettingsService());

    public void Dispose()
    {
        _sessionManager.Dispose();
    }

    [Fact]
    public void AddStateListener_CalledOnSessionCreate()
    {
        var callCount = 0;
        _sessionManager.AddStateListener(() => callCount++);

        _sessionManager.CreateSession();

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void AddStateListener_CalledOnSessionClose()
    {
        var session = _sessionManager.CreateSession();
        var callCount = 0;
        _sessionManager.AddStateListener(() => callCount++);

        _sessionManager.CloseSession(session.Id);

        // Close triggers at least 1 notification (may be more due to PTY shutdown)
        Assert.True(callCount >= 1, $"Expected at least 1 notification, got {callCount}");
    }

    [Fact]
    public void RemoveStateListener_StopsNotifications()
    {
        var callCount = 0;
        var listenerId = _sessionManager.AddStateListener(() => callCount++);

        _sessionManager.CreateSession();
        Assert.Equal(1, callCount);

        _sessionManager.RemoveStateListener(listenerId);
        _sessionManager.CreateSession();

        Assert.Equal(1, callCount); // Should not increase
    }

    [Fact]
    public void MultipleListeners_AllNotified()
    {
        var count1 = 0;
        var count2 = 0;
        _sessionManager.AddStateListener(() => count1++);
        _sessionManager.AddStateListener(() => count2++);

        _sessionManager.CreateSession();

        Assert.Equal(1, count1);
        Assert.Equal(1, count2);
    }

    [Fact]
    public void StateListener_FailureDoesNotAffectOthers()
    {
        var count = 0;
        _sessionManager.AddStateListener(() => throw new Exception("Listener failure"));
        _sessionManager.AddStateListener(() => count++);

        _sessionManager.CreateSession();

        Assert.Equal(1, count);
    }
}
