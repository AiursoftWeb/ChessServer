using Aiursoft.Canon.BackgroundJobs;

namespace Aiursoft.ChessServer.Services.BackgroundJobs;

public class ExpiredGamesCleanupJob(GameSessionService sessions) : IBackgroundJob
{
    public string Name => "Expired chess rooms";
    public string Description => "Update chess clocks, archive finished games and release inactive rooms.";
    public Task ExecuteAsync() => sessions.TickAsync();
}
