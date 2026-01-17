using Ai.Tlbx.MidTerm.Models;

namespace Ai.Tlbx.MidTerm.Services;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(WebApplication app, HistoryService historyService, TtyHostSessionManager sessionManager)
    {
        app.MapGet("/api/history", () =>
        {
            return Results.Json(historyService.GetEntries(), AppJsonContext.Default.ListLaunchEntry);
        });

        app.MapPut("/api/history/{id}/star", (string id) =>
        {
            if (historyService.ToggleStar(id))
            {
                return Results.Ok();
            }
            return Results.NotFound();
        });

        app.MapDelete("/api/history/{id}", (string id) =>
        {
            if (historyService.RemoveEntry(id))
            {
                return Results.Ok();
            }
            return Results.NotFound();
        });

        // Debug endpoint to manually snapshot current session state to history
        app.MapPost("/api/sessions/{sessionId}/snapshot", (string sessionId) =>
        {
            var session = sessionManager.GetSession(sessionId);
            if (session is null)
            {
                return Results.NotFound(new { error = "Session not found" });
            }

            // Return debug info about what we're seeing
            var debugInfo = new HistorySnapshotResult
            {
                SessionId = sessionId,
                ShellType = session.ShellType,
                CurrentDirectory = session.CurrentDirectory,
                ForegroundPid = session.ForegroundPid,
                ForegroundName = session.ForegroundName,
                ForegroundCommandLine = session.ForegroundCommandLine,
                Recorded = false,
                SkipReason = null
            };

            // Check if we have foreground info to record
            if (string.IsNullOrEmpty(session.ForegroundName))
            {
                debugInfo.SkipReason = "ForegroundName is empty (no subprocess detected)";
                return Results.Json(debugInfo, AppJsonContext.Default.HistorySnapshotResult);
            }

            if (string.IsNullOrEmpty(session.CurrentDirectory))
            {
                debugInfo.SkipReason = "CurrentDirectory is empty";
                return Results.Json(debugInfo, AppJsonContext.Default.HistorySnapshotResult);
            }

            // Check shell-skip filter
            if (session.ForegroundName.Equals(session.ShellType, StringComparison.OrdinalIgnoreCase))
            {
                debugInfo.SkipReason = $"ForegroundName '{session.ForegroundName}' matches ShellType '{session.ShellType}'";
                return Results.Json(debugInfo, AppJsonContext.Default.HistorySnapshotResult);
            }

            // Record it
            historyService.RecordEntry(
                session.ShellType,
                session.ForegroundName,
                session.ForegroundCommandLine,
                session.CurrentDirectory);

            debugInfo.Recorded = true;
            return Results.Json(debugInfo, AppJsonContext.Default.HistorySnapshotResult);
        });
    }
}

public sealed class HistorySnapshotResult
{
    public string SessionId { get; set; } = "";
    public string ShellType { get; set; } = "";
    public string? CurrentDirectory { get; set; }
    public int? ForegroundPid { get; set; }
    public string? ForegroundName { get; set; }
    public string? ForegroundCommandLine { get; set; }
    public bool Recorded { get; set; }
    public string? SkipReason { get; set; }
}
