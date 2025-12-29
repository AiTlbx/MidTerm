using Ai.Tlbx.MiddleManager.Settings;
using Ai.Tlbx.MiddleManager.Shells;
using Ai.Tlbx.MiddleManager.Services;
using Xunit;

namespace Ai.Tlbx.MiddleManager.Tests;

public class ResizeTests : IDisposable
{
    private readonly SessionManager _sessionManager = new(new ShellRegistry(), new SettingsService());

    public void Dispose()
    {
        _sessionManager.Dispose();
    }

    [Fact]
    public void Resize_UpdatesDimensions()
    {
        var session = _sessionManager.CreateSession(cols: 80, rows: 24);

        var accepted = session.Resize(120, 40);

        Assert.True(accepted);
        Assert.Equal(120, session.Cols);
        Assert.Equal(40, session.Rows);
    }

    [Fact]
    public void Resize_SameDimensions_ReturnsTrue()
    {
        var session = _sessionManager.CreateSession(cols: 80, rows: 24);

        var accepted = session.Resize(80, 24);

        Assert.True(accepted);
    }

    [Fact]
    public void Resize_ActiveViewerOnly_AcceptsFromActiveViewer()
    {
        var session = _sessionManager.CreateSession(cols: 80, rows: 24);

        // First viewer becomes active
        session.Resize(100, 30, "viewer1");
        Assert.Equal(100, session.Cols);
        Assert.Equal(30, session.Rows);

        // Active viewer can resize
        var accepted = session.Resize(120, 40, "viewer1");
        Assert.True(accepted);
        Assert.Equal(120, session.Cols);
        Assert.Equal(40, session.Rows);
    }

    [Fact]
    public void Resize_InactiveViewer_Rejected()
    {
        var session = _sessionManager.CreateSession(cols: 80, rows: 24);

        // First viewer becomes active via input
        _ = session.SendInputAsync("test", "viewer1");

        // Different viewer tries to resize - should be rejected
        var accepted = session.Resize(120, 40, "viewer2");

        Assert.False(accepted);
        Assert.Equal(80, session.Cols);
        Assert.Equal(24, session.Rows);
    }

    [Fact]
    public void Resize_NoViewerId_AlwaysAccepted()
    {
        var session = _sessionManager.CreateSession(cols: 80, rows: 24);

        // Set an active viewer
        _ = session.SendInputAsync("test", "viewer1");

        // Resize without viewerId should work (for API calls)
        var accepted = session.Resize(120, 40, null);

        Assert.True(accepted);
        Assert.Equal(120, session.Cols);
        Assert.Equal(40, session.Rows);
    }

    [Fact]
    public void Resize_StateChangeNotified()
    {
        var stateChanged = false;
        var session = _sessionManager.CreateSession(cols: 80, rows: 24);
        session.OnStateChanged += () => stateChanged = true;

        session.Resize(120, 40);

        Assert.True(stateChanged);
    }
}
