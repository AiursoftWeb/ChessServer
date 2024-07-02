using System.Threading.Channels;
using Lynx;
using Lynx.Model;

namespace Aiursoft.ChessServer.Services;

public class ChessEngine
{
    private readonly Engine _engine;

    public ChessEngine()
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        _engine = new Engine(channel.Writer);
    }

    public string GetBestMove(string fen)
    {
        _engine.AdjustPosition($"position fen {fen}");
        return _engine.IDDFS(1, int.MaxValue)
            .BestMove
            .ToEPDString();
    }
}