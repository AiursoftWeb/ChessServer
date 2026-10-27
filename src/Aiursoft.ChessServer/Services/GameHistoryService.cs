using Aiursoft.ChessServer.Entities;
using Aiursoft.ChessServer.Models;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.ChessServer.Services;

public class GameHistoryService(TemplateDbContext context) : IScopedDependency
{
    public async Task SaveAsync(AcceptedChallenge room, bool deferCommit = false)
    {
        await room.ArchiveLock.WaitAsync();
        try
        {
            if (room.Archived) return;
            CompletedGame record;
            lock (room.Game.MovePieceLock)
            {
                if (room.Game.Board.IsEndGame) room.FinishedAt ??= DateTimeOffset.UtcNow;
                if (!room.Game.Board.IsEndGame) return;
                record = new CompletedGame
                {
                    Id = room.HistoryId,
                    WhiteUserId = room.GetWhitePlayer().Id.ToString(), BlackUserId = room.GetBlackPlayer().Id.ToString(),
                    WhiteName = room.GetWhitePlayer().NickName, BlackName = room.GetBlackPlayer().NickName,
                    Result = room.Result, EndReason = room.OutcomeReason ?? room.Game.Board.EndGame!.EndgameType.ToString(),
                    StartedAt = room.GameStartTime, FinishedAt = room.FinishedAt!.Value.UtcDateTime, Pgn = room.Pgn()
                };
            }
            if (!await context.CompletedGames.AnyAsync(g => g.Id == record.Id))
            {
                context.CompletedGames.Add(record);
            }
            if (!deferCommit)
            {
                await context.SaveChangesAsync();
                room.Archived = true;
            }
        }
        finally { room.ArchiveLock.Release(); }
    }
}
