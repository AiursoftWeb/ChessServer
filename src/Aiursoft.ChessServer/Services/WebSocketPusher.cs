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

    public async Task Send(string message)
    {
        if (_dropped)
        {
            throw new InvalidOperationException("WebSocket is dropped!");
        }
        try
        {
            await (_ws?.SendMessage(message) ?? throw new InvalidOperationException("WebSocket is not connected!"));
        }
        catch (WebSocketException)
        {
            _dropped = true;
        }
    }

    public async Task Wait()
    {
        try
        {
            var buffer = new byte[1024 * 4];
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
        catch (WebSocketException)
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

        _dropped = true;
        
        return Task.CompletedTask;
    }
}