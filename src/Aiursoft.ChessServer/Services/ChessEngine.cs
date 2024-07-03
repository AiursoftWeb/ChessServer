using System.Threading.Channels;
using Chess;
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
        var board = ChessBoard.LoadFromFen(fen);
        
        _engine.AdjustPosition($"position fen {fen}");
        var moves = _engine.IDDFS(1, 10);
        var bestMove = moves.Moves
            .Select(t => t.ToEPDString())
            .FirstOrDefault(t => board.IsValidMove(t));
        return bestMove ?? moves.BestMove.ToEPDString();
    }
}