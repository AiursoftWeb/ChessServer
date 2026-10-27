using Aiursoft.ChessServer.Models;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.ChessServer.Data;

public class InMemoryDatabase : ISingletonDependency
{
    private readonly Dictionary<int, Challenge> _challenges = new();
    private readonly object _gate = new();
    private int _nextId;

    public IReadOnlyCollection<KeyValuePair<int, Challenge>> GetPublicOpenChallenges()
    {
        lock (_gate) return _challenges.Where(p => p.Value is not AcceptedChallenge && p.Value.Permission == ChallengePermission.Public).ToArray();
    }

    public IReadOnlyCollection<KeyValuePair<int, Challenge>> GetOnGoingOpenChallenges()
    {
        lock (_gate) return _challenges.Where(p => p.Value is AcceptedChallenge && p.Value.Permission == ChallengePermission.Public).ToArray();
    }

    public int? GetMyOpenChallenge(Guid playerId)
    {
        lock (_gate) return _challenges.Where(p => p.Value.Creator.Id == playerId && p.Value is not AcceptedChallenge).Select(p => (int?)p.Key).FirstOrDefault();
    }

    public Challenge? GetChallenge(int id)
    {
        lock (_gate) return _challenges.GetValueOrDefault(id);
    }

    public AcceptedChallenge? GetAcceptedChallenge(int id) => GetChallenge(id) as AcceptedChallenge;

    public int CreateChallenge(Challenge challenge)
    {
        lock (_gate)
        {
            if (_challenges.Count >= 256) throw new InvalidOperationException("All rooms are busy. Please try again later.");
            if (_challenges.Values.Any(c => c.Creator.Id == challenge.Creator.Id && c is not AcceptedChallenge))
                throw new InvalidOperationException("You already have an open challenge.");
            var id = ++_nextId;
            _challenges.Add(id, challenge);
            return id;
        }
    }

    public async Task AcceptAsync(int id, Player accepter)
    {
        AcceptedChallenge accepted;
        lock (_gate)
        {
            var challenge = _challenges.GetValueOrDefault(id) ?? throw new InvalidOperationException("Challenge not found.");
            if (challenge is AcceptedChallenge || challenge.Creator.Id == accepter.Id)
                throw new InvalidOperationException("This challenge cannot be accepted.");
            accepted = new AcceptedChallenge(challenge.Creator, accepter, challenge.Message, challenge.RoleRule,
                challenge.TimeLimit, challenge.Permission, challenge.ChallengeChangedChannel)
            { Difficulty = challenge.Difficulty, LifetimeSource = challenge.LifetimeSource };
            _challenges[id] = accepted;
        }
        await accepted.ChallengeChangedChannel.BroadcastAsync("game-started");
    }

    public void DeleteChallenge(int id)
    {
        Challenge? removed;
        lock (_gate) { _challenges.Remove(id, out removed); }
        removed?.End();
    }

    public int RemoveExpired(DateTimeOffset now)
    {
        KeyValuePair<int, Challenge>[] expired;
        lock (_gate)
        {
            expired = _challenges.Where(p => now - p.Value.LastActivity > (p.Value is AcceptedChallenge
                ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(10))).ToArray();
            foreach (var pair in expired) _challenges.Remove(pair.Key);
        }
        foreach (var pair in expired) pair.Value.End();
        return expired.Length;
    }
}
