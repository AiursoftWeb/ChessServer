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
        var positionClone = new Position(_engine.Game.CurrentPosition);

        var result = _engine.IDDFS(1, 10);
        _engine.Game.ResetCurrentPositionToBeforeSearchState();

        return result
            .BestMove
            .ToEPDString(positionClone);
    }
}