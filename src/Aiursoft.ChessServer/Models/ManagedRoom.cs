using Aiursoft.AiurObserver;

namespace Aiursoft.ChessServer.Models;

public class RoomSeat
{
    public required Player Player { get; set; }
    public string Kind { get; set; } = "human";
    public int? Difficulty { get; set; }
    public string? Model { get; set; }
    public bool IsAi => Kind != "human";
    public string Description => Kind == "llm" ? $"LLM — {Model}" : Kind == "engine" ? $"Lynx — level {Difficulty}" : Player.NickName;
}

public class RoomMember
{
    public required Player Player { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SeenAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ManagedRoom(Player host) : Challenge(host, "Chess room", RoleRule.CreatorWhite, TimeSpan.MaxValue, ChallengePermission.Public)
{
    public AsyncObservable<ChatMessage> ChatChannel { get; } = new();
    public List<RoomMember> Members { get; set; } = [new() { Player = host }];
    public RoomSeat? White { get; set; } = new() { Player = host };
    public RoomSeat? Black { get; set; }
    public HashSet<Guid> Kicked { get; set; } = [];
    public int? CurrentGameId { get; set; }
    public bool RandomColors { get; set; }
    public long Version { get; set; }
    public List<Guid> Commands { get; set; } = [];
}

public class ManagedRoomSnapshot
{
    public List<RoomMember> Members { get; set; } = [];
    public RoomSeat? White { get; set; }
    public RoomSeat? Black { get; set; }
    public HashSet<Guid> Kicked { get; set; } = [];
    public int? CurrentGameId { get; set; }
    public bool RandomColors { get; set; }
    public long Version { get; set; }
    public List<Guid> Commands { get; set; } = [];
}

public class AiTurnRecord
{
    public int Ply { get; set; }
    public string Controller { get; set; } = "";
    public long Milliseconds { get; set; }
    public string? EngineVersion { get; set; }
    public int Attempts { get; set; }
}
