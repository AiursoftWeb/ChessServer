using Aiursoft.Scanner.Abstractions;
using Aiursoft.ChessServer.Models;
using Aiursoft.InMemoryKvDb.AutoCreate;
using Aiursoft.InMemoryKvDb.ManualCreate;

namespace Aiursoft.ChessServer.Data;

// ReSharper disable once ClassNeverInstantiated.Global
public class InMemoryDatabase(
    LruMemoryStore<Player, Guid> playersDb,
    LruMemoryStoreManualCreated<Challenge, int> challenges) : ISingletonDependency
{
    private IEnumerable<KeyValuePair<int, Challenge>> OpenChallenges =>
        challenges.GetAllWithKeys().Where(t => t.Value is not AcceptedChallenge);
    
    private IEnumerable<KeyValuePair<int, Challenge>> OnGoingChallenges =>
        challenges.GetAllWithKeys().Where(t => t.Value is AcceptedChallenge);

    public Player GetOrAddPlayer(Guid id)
    {
        return playersDb.GetOrAdd(id);
    }

    public IReadOnlyCollection<KeyValuePair<int, Challenge>> GetPublicOpenChallenges()
    {
        return OpenChallenges
            .Where(t => t.Value.Permission == ChallengePermission.Public)
            .ToArray();
    }
    
    public IReadOnlyCollection<KeyValuePair<int, Challenge>> GetOnGoingOpenChallenges()
    {
        return OnGoingChallenges
            .Where(t => t.Value.Permission == ChallengePermission.Public)
            .ToArray();
    }
    
    public int? GetMyOpenChallenge(Guid playerId)
    {
        if (OpenChallenges.All(t => t.Value.Creator.Id != playerId))
        {
            return null;
        }
        
        return OpenChallenges
            .FirstOrDefault(t => t.Value.Creator.Id == playerId)
            .Key;
    }
    
    public Challenge? GetChallenge(int id)
    {
        return challenges.Get(id);
    }
    
    public AcceptedChallenge? GetAcceptedChallenge(int id)
    {
        var challenge = GetChallenge(id);
        return challenge as AcceptedChallenge;
    }

    public async Task PatchChallengeAsAcceptedAsync(int id, Guid accepter)
    {
        var challenge = challenges.Get(id);
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
        
        challenges.AddToCache(id, newChallenge);
        await challenges.Get(id)!.ChallengeChangedChannel.BroadcastAsync("game-started");
    }
    
    public int? GetFirstPublicChallengeKey()
    {
        if (OpenChallenges.All(t => t.Value.Permission != ChallengePermission.Public))
        {
            return null;
        }
        
        return OpenChallenges
            .FirstOrDefault(t => t.Value.Permission == ChallengePermission.Public)
            .Key;
    }
    
    public void DeleteChallenge(int id)
    {
        challenges.Remove(id);
    }
    
    public void CreateChallenge(int id, Challenge challenge)
    {
        challenges.AddToCache(id, challenge);
    }
}