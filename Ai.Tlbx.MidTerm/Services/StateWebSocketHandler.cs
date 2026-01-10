using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ai.Tlbx.MidTerm.Models;
using Ai.Tlbx.MidTerm.Models.Update;

namespace Ai.Tlbx.MidTerm.Services;

public sealed class StateWebSocketHandler
{
    private readonly TtyHostSessionManager _sessionManager;
    private readonly UpdateService _updateService;

    public StateWebSocketHandler(
        TtyHostSessionManager sessionManager,
        UpdateService updateService)
    {
        _sessionManager = sessionManager;
        _updateService = updateService;
    }

    public async Task HandleAsync(HttpContext context)
    {
        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var sendLock = new SemaphoreSlim(1, 1);
        UpdateInfo? lastUpdate = null;

        async Task SendStateAsync()
        {
            if (ws.State != WebSocketState.Open)
            {
                return;
            }

            await sendLock.WaitAsync();
            try
            {
                if (ws.State != WebSocketState.Open)
                {
                    return;
                }

                var sessionList = _sessionManager.GetSessionList();
                var state = new StateUpdate
                {
                    Sessions = sessionList,
                    Update = lastUpdate
                };
                var json = JsonSerializer.Serialize(state, AppJsonContext.Default.StateUpdate);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
            }
            finally
            {
                sendLock.Release();
            }
        }

        void OnStateChange() => _ = SendStateAsync();

        void OnUpdateAvailable(UpdateInfo update)
        {
            lastUpdate = update;
            _ = SendStateAsync();
        }

        var sessionListenerId = _sessionManager.AddStateListener(OnStateChange);
        var updateListenerId = _updateService.AddUpdateListener(OnUpdateAvailable);

        try
        {
            lastUpdate = _updateService.LatestUpdate;
            await SendStateAsync();

            var buffer = new byte[1024];
            while (ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
                catch
                {
                    break;
                }
            }
        }
        finally
        {
            _sessionManager.RemoveStateListener(sessionListenerId);
            _updateService.RemoveUpdateListener(updateListenerId);
            sendLock.Dispose();

            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }
}
