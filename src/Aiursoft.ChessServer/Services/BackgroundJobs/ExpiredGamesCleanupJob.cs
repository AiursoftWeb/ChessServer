using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.ChessServer.Data;

namespace Aiursoft.ChessServer.Services.BackgroundJobs;

public class ExpiredGamesCleanupJob(InMemoryDatabase database) : IBackgroundJob
{
    public string Name => "Expired chess rooms";
    public string Description => "Release inactive games and their WebSocket subscriptions.";
    public Task ExecuteAsync()
    {
        database.RemoveExpired(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}
