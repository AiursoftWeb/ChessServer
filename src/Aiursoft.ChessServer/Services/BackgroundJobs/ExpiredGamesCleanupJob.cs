using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.ChessServer.Data;

namespace Aiursoft.ChessServer.Services.BackgroundJobs;

public class ExpiredGamesCleanupJob(InMemoryDatabase database, GameHistoryService history) : IBackgroundJob
{
    public string Name => "Expired chess rooms";
    public string Description => "Release inactive games and their WebSocket subscriptions.";
    public async Task ExecuteAsync()
    {
        foreach (var room in database.GetGames())
        {
            var ended = room.UpdateClock(DateTimeOffset.UtcNow);
            if (ended) await room.Game.FenChangedChannel.BroadcastAsync(room.Game.Board.ToFen());
            if (room.Game.Board.IsEndGame) await history.SaveAsync(room);
        }
        database.RemoveExpired(DateTimeOffset.UtcNow);

    }
}
