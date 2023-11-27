using System.Net.WebSockets;
using Aiursoft.CSTools.Tools;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.ChessServer.Services;

public class WebSocketPusher : IScopedDependency
{
    private bool _dropped;
    private WebSocket? _ws;

    public bool Connected => !_dropped && _ws?.State == WebSocketState.Open;

    public async Task Accept(HttpContext context)
    {
        _ws = await context.WebSockets.AcceptWebSocketAsync();
    }

    public async Task SendMessage(string message)
    {
        await (_ws?.SendMessage(message) ?? throw new InvalidOperationException("WebSocket is not connected!"));
    }

    public async Task PendingClose()
    {
        try
        {
            var buffer = new ArraySegment<byte>(new byte[4096 * 20]);
            while (true)
            {
                await (_ws?.ReceiveAsync(buffer, CancellationToken.None) ?? throw new InvalidOperationException("WebSocket is not connected!"));
                if (_ws.State == WebSocketState.Open)
                {
                    continue;
                }

                _dropped = true;
                return;
            }
        }
        catch (Exception e) when (!e.Message.StartsWith("The remote party closed the WebSocket connection"))
        {
            _dropped = true;
        }
    }

    public Task Close()
    {
        if (_ws?.State == WebSocketState.Open)
        {
            return _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Close because of error.",
                CancellationToken.None);
        }
        
        return Task.CompletedTask;
    }
}