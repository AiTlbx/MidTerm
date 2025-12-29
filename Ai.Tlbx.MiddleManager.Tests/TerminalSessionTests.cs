using System.Text;
using Ai.Tlbx.MiddleManager.Settings;
using Ai.Tlbx.MiddleManager.Shells;
using Ai.Tlbx.MiddleManager.Services;
using Xunit;

namespace Ai.Tlbx.MiddleManager.Tests;

public class TerminalSessionTests : IDisposable
{
    private readonly SessionManager _sessionManager = new(new ShellRegistry(), new SettingsService());

    public void Dispose()
    {
        _sessionManager.Dispose();
    }

    [Fact]
    public void CreateSession_ReturnsRunningSession()
    {
        var session = _sessionManager.CreateSession();

        Assert.NotNull(session);
        Assert.NotEmpty(session.Id);
        Assert.True(session.IsRunning);
        Assert.True(session.Pid > 0);
    }

    [Fact]
    public void CreateSession_WithCustomSize_SetsCorrectDimensions()
    {
        var session = _sessionManager.CreateSession(cols: 80, rows: 24);

        Assert.Equal(80, session.Cols);
        Assert.Equal(24, session.Rows);
    }

    [Fact]
    public void GetSession_ReturnsCorrectSession()
    {
        var session = _sessionManager.CreateSession();

        var retrieved = _sessionManager.GetSession(session.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(session.Id, retrieved.Id);
    }

    [Fact]
    public void GetSession_WithInvalidId_ReturnsNull()
    {
        var retrieved = _sessionManager.GetSession("nonexistent");

        Assert.Null(retrieved);
    }

    [Fact]
    public void CloseSession_RemovesSession()
    {
        var session = _sessionManager.CreateSession();
        var id = session.Id;

        _sessionManager.CloseSession(id);

        Assert.Null(_sessionManager.GetSession(id));
    }

    [Fact]
    public void Sessions_ReturnsAllSessions()
    {
        var session1 = _sessionManager.CreateSession();
        var session2 = _sessionManager.CreateSession();

        var sessions = _sessionManager.Sessions;

        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.Id == session1.Id);
        Assert.Contains(sessions, s => s.Id == session2.Id);
    }

    [Fact]
    public void GetSessionList_ReturnsCorrectDtos()
    {
        var session = _sessionManager.CreateSession(cols: 100, rows: 40);

        var list = _sessionManager.GetSessionList();

        Assert.Single(list.Sessions);
        var dto = list.Sessions[0];
        Assert.Equal(session.Id, dto.Id);
        Assert.Equal(session.Pid, dto.Pid);
        Assert.Equal(100, dto.Cols);
        Assert.Equal(40, dto.Rows);
        Assert.True(dto.IsRunning);
    }
}
