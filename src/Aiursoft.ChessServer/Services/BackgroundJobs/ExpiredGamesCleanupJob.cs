using Aiursoft.Canon.BackgroundJobs;

namespace Aiursoft.ChessServer.Services.BackgroundJobs;

public class ExpiredGamesCleanupJob(GameSessionService sessions, ManagedRoomService rooms, ComputerTurnCoordinator computers, Data.InMemoryDatabase database) : IBackgroundJob
{
    public string Name => "Expired chess rooms";
    public string Description => "Update chess clocks, archive finished games and release inactive rooms.";
    public async Task ExecuteAsync()
    {
        await rooms.TickAsync();
        await sessions.TickAsync();
        foreach (var pair in database.GetRooms())
            if (pair.Value is Models.AcceptedChallenge { ParentRoomId: not null }) computers.Wake(pair.Key);
    }
}
