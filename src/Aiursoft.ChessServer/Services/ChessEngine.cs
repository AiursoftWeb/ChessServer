using System.Threading.Channels;
using Aiursoft.Scanner.Abstractions;
using Lynx;
using Lynx.Model;

namespace Aiursoft.ChessServer.Services;

public class ChessEngine(Random? random = null) : ISingletonDependency, IDisposable
{
    private readonly SemaphoreSlim _slots = new(2);
    public void Dispose() => _slots.Dispose();

    public static (int Depth, int Milliseconds) Limits(int difficulty) => difficulty switch
    {
        1 or 2 or 3 or 4 => (1, 100),
        5 => (4, 600),
        6 => (6, 900),
        7 => (9, 1300),
        8 => (12, 2000),
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
            if (difficulty <= 4) return ChooseTrainingMove(fen, difficulty, generator, token);
            var output = Channel.CreateBounded<object>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
            using var engine = new Engine(output.Writer);
            engine.AdjustPosition($"position fen {fen}");
            using var position = new Position(engine.Game.CurrentPosition);
            var constraints = new SearchConstraints(milliseconds, milliseconds, depth);
            var result = engine.BestMove(in constraints, false, token, token);
            token.ThrowIfCancellationRequested();
            return result.BestMove.ToEPDString(position);
        }, token);
        }
        finally { _slots.Release(); }
    }
    private static string ChooseTrainingMove(string fen, int difficulty, Random random, CancellationToken token)
    {
        // Every candidate receives the same shallow reply search. Lower levels may choose
        // weaker candidates within a bounded score loss instead of alternating between
        // an unrestricted random move and a strong engine move.
        var board = Chess.ChessBoard.LoadFromFen(fen);
        var candidates = new List<(string Move, int Score)>();
        var output = Channel.CreateBounded<object>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
        using var engine = new Engine(output.Writer);
        var (depth, milliseconds) = Limits(difficulty);
        foreach (var move in board.Moves())
        {
            token.ThrowIfCancellationRequested();
            var next = Chess.ChessBoard.LoadFromFen(fen);
            next.Move(move);
            int score;
            if (next.IsEndGame)
                score = next.EndGame!.WonSide == board.Turn ? 30000 : next.EndGame.WonSide == null ? 0 : -30000;
            else
            {
                engine.AdjustPosition($"position fen {next.ToFen()}");
                var constraints = new SearchConstraints(milliseconds, milliseconds, depth);
                var result = engine.BestMove(in constraints, false, token, token);
                score = result.Mate != 0 ? result.Mate > 0 ? -29000 : 29000 : -result.Score;
            }
            candidates.Add((next.MovesToSan.Last(), score));
        }
        token.ThrowIfCancellationRequested();
        if (candidates.Count == 0) throw new InvalidOperationException("The game has ended.");
        var allowance = difficulty switch { 1 => 900, 2 => 450, 3 => 200, _ => 60 };
        var best = candidates.Max(c => c.Score);
        var choices = candidates.Where(c => c.Score >= best - allowance).ToArray();
        return choices[random.Next(choices.Length)].Move;
    }

}
