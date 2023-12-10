using System.Net.WebSockets;
using System.Text;

namespace Aiursoft.ChessServer.Tests;

public class WebSocketTester
{
    public string LastMessage { get; private set; } = string.Empty;

    public async Task Monitor(WebSocket socket)
    {
        var buffer = new ArraySegment<byte>(new byte[2048]);
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            switch (result.MessageType)
            {
                case WebSocketMessageType.Close:
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    return;
                case WebSocketMessageType.Text:
                    LastMessage = Encoding.UTF8.GetString(buffer.Array!, 0, result.Count);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}