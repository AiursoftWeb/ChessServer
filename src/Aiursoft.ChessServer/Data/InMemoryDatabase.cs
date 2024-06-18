using Aiursoft.Scanner.Abstractions;
using System.Collections.Concurrent;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Models.ViewModels;

namespace Aiursoft.ChessServer.Data;

public class InMemoryDatabase : ISingletonDependency
{
    private ConcurrentDictionary<Guid, Player> Players { get; } = new();
    
    private ConcurrentDictionary<int, Challenge> Challenges { get; } = new();

    private IEnumerable<KeyValuePair<int, Challenge>> OpenChallenges =>
        Challenges.Where(t => t.Value is not AcceptedChallenge);
    
    private IEnumerable<KeyValuePair<int, Challenge>> OnGoingChallenges =>
        Challenges.Where(t => t.Value is AcceptedChallenge);

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

    public IReadOnlyCollection<KeyValuePair<int, Challenge>> GetPublicOpenChallenges()
    {
        lock (Challenges)
        {
            return OpenChallenges
                .Where(t => t.Value.Permission == ChallengePermission.Public)
                .ToArray();
        }
    }
    
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> GetOnGoingOpenChallenges()
    {
        lock (Challenges)
        {
            return OnGoingChallenges
                .Where(t => t.Value.Permission == ChallengePermission.Public)
                .ToArray();
        }
    }
    
    public int? GetMyOpenChallenge(Guid playerId)
    {
        lock (Challenges)
        {
            if (OpenChallenges.All(t => t.Value.Creator.Id != playerId))
            {
                return null;
            }
            
            return OpenChallenges
                .FirstOrDefault(t => t.Value.Creator.Id == playerId)
                .Key;
        }
    }
    
    public Challenge? GetChallenge(int id)
    {
        lock (Challenges)
        {
            return Challenges.GetValueOrDefault(id);
        }
    }
    
    public AcceptedChallenge? GetAcceptedChallenge(int id)
    {
        var challenge = GetChallenge(id);
        return challenge as AcceptedChallenge;
    }

    public async Task PatchChallengeAsAcceptedAsync(int id, Guid accepter)
    {
        lock (Challenges)
        {
            var challenge = Challenges.GetValueOrDefault(id);
            if (challenge == null)
            {
                throw new InvalidOperationException("Challenge not found!");
            }
            if (challenge.Creator.Id == accepter)
            {
                throw new InvalidOperationException("Cannot accept your own challenge!");
            }
            if (challenge is AcceptedChallenge)
            {
                throw new InvalidOperationException("Challenge already accepted!");
            }
            
            var newChallenge = new AcceptedChallenge(
                creator: challenge.Creator,
                accepter: GetOrAddPlayer(accepter),
                message: challenge.Message,
                roleRule: challenge.RoleRule,
                timeLimit: challenge.TimeLimit,
                permission: challenge.Permission,
                challengeChangedChannel: challenge.ChallengeChangedChannel);
            
            Challenges[id] = newChallenge;
        }
        await Challenges.GetValueOrDefault(id)!.ChallengeChangedChannel.BroadcastAsync("game-started");
    }
    
    public int? GetFirstPublicChallengeKey()
    {
        lock (Challenges)
        {
            if (OpenChallenges.All(t => t.Value.Permission != ChallengePermission.Public))
            {
                return null;
            }
            
            return OpenChallenges
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