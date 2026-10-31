using System.Diagnostics;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.ChessServer.Services;

public class ComputerTurnCoordinator(InMemoryDatabase database, IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime, ILogger<ComputerTurnCoordinator> logger) : ISingletonDependency
{
    public void Wake(int id)
    {
        if (database.GetAcceptedChallenge(id) is not { ParentRoomId: not null } game) return;
        CancellationTokenSource search;
        lock (game.Game.MovePieceLock)
        {
            if (game.Game.IsThinking || game.Game.Board.IsEndGame || game.Game.ComputerError != null || game.ControllerFor(game.GetTurnPlayer().Id)?.IsAi != true) return;
            search = CancellationTokenSource.CreateLinkedTokenSource(game.Lifetime, lifetime.ApplicationStopping);
            game.Game.IsThinking = true; game.Game.SearchCancellation = search;
        }
        _ = RunAsync(id, game, search);
    }

    private async Task RunAsync(int id, AcceptedChallenge game, CancellationTokenSource search)
    {
        try
        {
            while (!search.IsCancellationRequested)
            {
                string fen, pgn; int ply; long revision; RoomSeat controller; TimeSpan remaining;
                lock (game.Game.MovePieceLock)
                {
                    if (game.Game.Board.IsEndGame || game.Game.ComputerError != null || game.ControllerFor(game.GetTurnPlayer().Id) is not { IsAi: true } seat) return;
                    controller = seat; fen = game.Game.Board.ToFen(); pgn = game.Pgn(); ply = game.Game.Board.MovesToSan.Count; revision = game.Revision;
                    remaining = game.Game.Board.Turn == Chess.PieceColor.White ? game.WhiteRemaining : game.BlackRemaining;
                }
                if (database.GetChallenge(game.ParentRoomId!.Value) is not ManagedRoom { Members.Count: > 0 }) return;
                using var scope = scopes.CreateScope();
                var sessions = scope.ServiceProvider.GetRequiredService<GameSessionService>();
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(search.Token);
                budget.CancelAfter(game.IsTimed ? remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1) : TimeSpan.FromMinutes(5));
                var clock = Stopwatch.StartNew();
                try
                {
                    string move; var attempts = 1;
                    if (controller.Kind == "llm")
                    {
                        var chosen = await scope.ServiceProvider.GetRequiredService<LlmChessPlayer>().GetMoveResultAsync(fen, pgn, controller.Model!, budget.Token);
                        move = chosen.Move; attempts = chosen.Attempts;
                    }
                    else move = await scope.ServiceProvider.GetRequiredService<ChessEngine>().GetBestMoveAsync(fen, controller.Difficulty!.Value, budget.Token);
                    var result = await sessions.ExecuteAsync(id, controller.Player.Id, new GameCommand(Guid.NewGuid(), "move", ply, move, revision), computer: true,
                        aiTurn: new AiTurnRecord { Ply = ply + 1, Controller = controller.Description, Milliseconds = clock.ElapsedMilliseconds, EngineVersion = controller.Kind == "engine" ? typeof(Lynx.Engine).Assembly.GetName().Version?.ToString() : null, Attempts = attempts });
                    if (result.Error != null) return;
                }
                catch (OperationCanceledException) when (search.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    logger.LogWarning("Computer turn failed in game {GameId} ({FailureType}).", id, exception.GetType().Name);
                    await sessions.ComputerFailedAsync(id, revision); return;
                }
            }
        }
        catch (Exception exception) { logger.LogError(exception, "Could not finalize computer turn in game {GameId}.", id); }
        finally
        {
            lock (game.Game.MovePieceLock)
                if (game.Game.SearchCancellation == search) { game.Game.IsThinking = false; game.Game.SearchCancellation = null; }
            search.Dispose();
        }
    }
}
