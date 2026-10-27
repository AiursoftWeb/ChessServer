using System.Threading.Channels;
using Aiursoft.Scanner.Abstractions;
using Lynx;
using Lynx.Model;
using Lynx.UCI.Commands.GUI;

namespace Aiursoft.ChessServer.Services;

public class ChessEngine(Random? random = null) : ISingletonDependency, IDisposable
{
    private readonly SemaphoreSlim _slots = new(2);
    public void Dispose() => _slots.Dispose();

    public static (int Depth, int Milliseconds) Limits(int difficulty) => difficulty switch
    {
        1 => (1, 100),
        2 => (2, 150),
        3 => (3, 250),
        4 => (4, 400),
        5 => (5, 600),
        6 => (6, 900),
        7 => (7, 1300),
        8 => (8, 2000),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
    };

    public async Task<string> GetBestMoveAsync(string fen, int difficulty, CancellationToken token)
    {
        await _slots.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var (depth, milliseconds) = Limits(difficulty);
            var generator = random ?? Random.Shared;
            var randomMovePercent = difficulty switch { 1 => 100, 2 => 70, 3 => 40, 4 => 15, _ => 0 };
            if (generator.Next(100) < randomMovePercent)
            {
                var board = Chess.ChessBoard.LoadFromFen(fen);
                var moves = board.Moves();
                if (moves.Length == 0) throw new InvalidOperationException("The game has ended.");
                board.Move(moves[generator.Next(moves.Length)]);
                return board.MovesToSan.Last();
            }
            var output = Channel.CreateBounded<object>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
            using var engine = new Engine(output.Writer);
            engine.AdjustPosition($"position fen {fen}");
            using var position = new Position(engine.Game.CurrentPosition);
            var constraints = TimeManager.CalculateTimeManagement(engine.Game, new GoCommand($"go depth {depth} movetime {milliseconds}"));
            var result = engine.BestMove(in constraints, false, token, token);
            token.ThrowIfCancellationRequested();
            return result.BestMove.ToEPDString(position);
        }, token);
        }
        finally { _slots.Release(); }
    }
}
