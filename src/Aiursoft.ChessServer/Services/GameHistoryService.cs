using Aiursoft.ChessServer.Entities;
using Aiursoft.ChessServer.Models;
using Aiursoft.Scanner.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.ChessServer.Services;

public class GameHistoryService(TemplateDbContext context) : IScopedDependency
{
    public Task<bool> BelongsToAsync(Guid historyId, Guid player)
    {
        var userId = player.ToString();
        return context.CompletedGames.AnyAsync(g => g.Id == historyId && (g.WhiteUserId == userId || g.BlackUserId == userId));
    }

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
                if (room.Creator.IsGuest && (room.IsComputer || room.Accepter.IsGuest)) return;
                record = new CompletedGame
                {
                    Id = room.HistoryId,
                    WhiteUserId = room.GetWhitePlayer().IsGuest ? "" : room.GetWhitePlayer().Id.ToString(),
                    BlackUserId = room.GetBlackPlayer().IsGuest ? "" : room.GetBlackPlayer().Id.ToString(),
                    WhiteName = room.GetWhitePlayer().NickName + (room.GetWhitePlayer().IsGuest ? " (Guest)" : ""),
                    BlackName = room.GetBlackPlayer().NickName + (room.GetBlackPlayer().IsGuest ? " (Guest)" : ""),
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
