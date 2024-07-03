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
            7 => "Unbeatable",
            _ => "Unknown"
        };
    }

    public string GetBestMove(string fen, int difficulty)
        // 1: Chimpanzee
        // 2: Easy
        // 3: Intermediate
        // 4: Hard
        // 5: Brutal
        // 6: Grandmaster
        // 7: Unbeatable
    {
        _engine.AdjustPosition($"position fen {fen}");
        var positionClone = new Position(_engine.Game.CurrentPosition);

        // Depth = difficulty, 50% of chance to be difficulty + 1
        var depth = difficulty + (new Random().Next(2) == 0 ? 1 : 0);
        var result = _engine.IDDFS(depth, 10);
        _engine.Game.ResetCurrentPositionToBeforeSearchState();

        if (difficulty > 4)
        {
            // If difficulty is higher than 5, we should use the best move
            return result.BestMove.ToEPDString(positionClone);
        }
        else
        {
            // Randomly choose one of the moves
            return result.Moves[new Random().Next(result.Moves.Count)].ToEPDString(positionClone);
        }
    }
}