using Aiursoft.Scanner.Abstractions;
using System.Collections.Concurrent;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;

namespace Aiursoft.ChessServer.Data;

public class InMemoryDatabase : ISingletonDependency
{
    private ConcurrentDictionary<int, Game> Games { get; } = new();
    
    private ConcurrentDictionary<Guid, Player> Players { get; } = new();
    
    private ConcurrentDictionary<int, Challenge> Challenges { get; } = new();

    public GameContext[] GetActiveGames()
    {
        return Games.Select(g => new GameContext(g.Value, g.Key)).ToArray();
    }

    public Game GetOrAddGame(int id)
    {
        lock (Games)
        {
            return Games.GetOrAdd(id, _ => new Game());
        }
    }

    public (int gameId, Game game) AddNewGameAndGetId()
    {
        lock (Games)
        {
            for (var id = 1;; id++)
            {
                if (Games.ContainsKey(id)) continue;
                var newGame = new Game();
                Games.TryAdd(id, newGame);
                return (id, newGame);
            }
        }
    }
    
    public Player GetOrAddPlayer(Guid id)
    {
        lock (Players)
        {
            return Players.GetOrAdd(id, _ => new Player(id)
            {
                NickName = "Anonymous " + new Random().Next(1000, 9999)
            });
        }
    }
    
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> GetPublicUnAcceptedChallenges()
    {
        lock (Challenges)
        {
            return Challenges
                .Where(t => t.Value.Permission == ChallengePermission.Public)
                .Where(t => t.Value.Accepter == null)
                .ToArray();
        }
    }
    
    public Challenge? GetChallenge(int id)
    {
        lock (Challenges)
        {
            return Challenges.GetValueOrDefault(id);
        }
    }
    
    public int? GetMyChallengeKey(Guid playerId)
    {
        lock (Challenges)
        {
            if (Challenges.All(t => t.Value.Creator.Id != playerId))
            {
                return null;
            }
            
            return Challenges
                .FirstOrDefault(t => t.Value.Creator.Id == playerId)
                .Key;
        }
    }
    
    public int? GetFirstPublicChallengeKey()
    {
        lock (Challenges)
        {
            if (Challenges.All(t => t.Value.Permission != ChallengePermission.Public))
            {
                return null;
            }
            
            return Challenges
                .FirstOrDefault(t => t.Value.Permission == ChallengePermission.Public)
                .Key;
        }
    }
    
    public void DeleteChallenge(int id)
    {
        lock (Challenges)
        {
            Challenges.TryRemove(id, out _);
        }
    }
    
    public void CreateChallenge(int id, Challenge challenge)
    {
        lock (Challenges)
        {
            var result = Challenges.TryAdd(id, challenge);
            if (!result)
            {
                throw new InvalidOperationException("Challenge already exists!");
            }
        }
    }
}