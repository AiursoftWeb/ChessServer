using System.Threading.Channels;
using Lynx;
using Lynx.Model;

namespace Aiursoft.ChessServer.Services;

public class ChessEngine
{
    private readonly Engine _engine;

    public ChessEngine()
    {
        var channel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        _engine = new Engine(channel.Writer);
    }
    
    public string GetComputerName(int difficulty)
    {
        return difficulty switch
        {
            1 => "Chimpanzee",
            2 => "Easy",
            3 => "Intermediate",
            4 => "Hard",
            5 => "Brutal",
            6 => "Grandmaster",
            7 => "Torture",
            8 => "Unbeatable",
            _ => "Unknown"
        };
    }

    public string GetBestMove(string fen, int difficulty)
    {
        _engine.AdjustPosition($"position fen {fen}");
        var positionClone = new Position(_engine.Game.CurrentPosition);

        return _engine.BestMove(new($"go depth {difficulty}"))
            .BestMove
            .ToEPDString(positionClone);
    }
}