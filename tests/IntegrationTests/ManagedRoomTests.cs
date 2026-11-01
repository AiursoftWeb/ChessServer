using System.Text.Json;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Entities;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.ChessServer.Tests.IntegrationTests;

[TestClass]
public class ManagedRoomTests : TestBase
{
    private ManagedRoomService Rooms => GetService<ManagedRoomService>();
    private InMemoryDatabase Database => GetService<InMemoryDatabase>();
    private static Player Human(string name) => new(Guid.NewGuid()) { NickName = name };
    private Task Act(int id, Player host, string kind, Guid? target = null, string? seat = null, bool confirm = false)
        => Rooms.ExecuteAsync(id, host, new RoomCommand { CommandId = Guid.NewGuid(), Version = Rooms.Find(id)!.Version,
            Kind = kind, Target = target, Seat = seat, Confirm = confirm });
    private Task AddEngine(int id, Player host, string seat) => Rooms.ExecuteAsync(id, host,
        new RoomCommand { CommandId = Guid.NewGuid(), Version = Rooms.Find(id)!.Version, Kind = "add-ai", Ai = "engine", Difficulty = 1, Seat = seat });

    [TestMethod]
    public async Task HostCanObserveTwoIndependentEnginesAndStartOneGame()
    {
        var host = Human("Organizer"); var id = await Rooms.CreateAsync(host);
        await Act(id, host, "move", host.Id, "observer");
        await AddEngine(id, host, "white"); await AddEngine(id, host, "black");
        await Act(id, host, "start");
        var room = Rooms.Find(id)!; var game = Database.GetAcceptedChallenge(room.CurrentGameId!.Value)!;
        Assert.AreEqual("m", game.GetPlayerColor(host.Id)); Assert.AreEqual(host.Id, room.Creator.Id);
        Assert.AreNotEqual(game.Creator.Id, game.Accepter.Id);
        Assert.AreEqual("engine", game.WhiteController!.Kind); Assert.AreEqual("engine", game.BlackController!.Kind);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Act(id, host, "start"));
        Assert.AreEqual(1, Database.GetGames().Length);
    }

    [TestMethod]
    public async Task OnlyHostCanConfigureAndAiCannotBeAnObserver()
    {
        var host = Human("Host"); var visitor = Human("Visitor"); var id = await Rooms.CreateAsync(host);
        await Rooms.JoinAsync(id, visitor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Act(id, visitor, "swap"));
        await AddEngine(id, host, "black"); var ai = Rooms.Find(id)!.Black!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Act(id, host, "move", ai.Player.Id, "observer"));
        Assert.AreEqual(2, Rooms.Find(id)!.Members.Count);
        Assert.AreEqual(ai.Player.Id, Rooms.Find(id)!.Black!.Player.Id);
    }

    [TestMethod]
    public async Task SeatSwapsAndHostTransferDoNotConflatePlayerAndOwner()
    {
        var host = Human("Host"); var other = Human("Other"); var id = await Rooms.CreateAsync(host);
        await Rooms.JoinAsync(id, other); await Act(id, host, "move", other.Id, "black");
        await Act(id, host, "move", other.Id, "white");
        Assert.AreEqual(other.Id, Rooms.Find(id)!.White!.Player.Id); Assert.AreEqual(host.Id, Rooms.Find(id)!.Black!.Player.Id);
        await Act(id, host, "transfer", other.Id);
        Assert.AreEqual(other.Id, Rooms.Find(id)!.Creator.Id);
        Assert.AreEqual(host.Id, Rooms.Find(id)!.Black!.Player.Id);
    }

    [TestMethod]
    public async Task HostLeavesNextHumanInheritsAndLastHumanClosesRoom()
    {
        var host = Human("First"); var second = Human("Second"); var third = Human("Third"); var id = await Rooms.CreateAsync(host);
        await Rooms.JoinAsync(id, second); await Rooms.JoinAsync(id, third);
        await Act(id, host, "leave"); Assert.AreEqual(second.Id, Rooms.Find(id)!.Creator.Id);
        await Act(id, second, "leave"); Assert.AreEqual(third.Id, Rooms.Find(id)!.Creator.Id);
        await Act(id, third, "leave"); Assert.IsNull(Rooms.Find(id));
    }

    [TestMethod]
    public async Task KickPreventsRejoiningAndCannotBeUsedByAnObserver()
    {
        var host = Human("Host"); var viewer = Human("Observer"); var id = await Rooms.CreateAsync(host);
        await Rooms.JoinAsync(id, viewer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Act(id, viewer, "kick", host.Id));
        await Act(id, host, "kick", viewer.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rooms.JoinAsync(id, viewer));
        Assert.IsTrue(Rooms.Blocked(id, viewer.Id)); Assert.AreEqual(1, Rooms.Find(id)!.Members.Count);
    }

    [TestMethod]
    public async Task RemovingAPlayingHumanRequiresConfirmationAndRecordsInterruption()
    {
        var host = Human("Host"); var player = Human("Opponent"); var id = await Rooms.CreateAsync(host);
        await Rooms.JoinAsync(id, player); await Act(id, host, "move", player.Id, "black"); await Act(id, host, "start");
        var gameId = Rooms.Find(id)!.CurrentGameId!.Value;
        var game = Database.GetAcceptedChallenge(gameId)!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Act(id, host, "kick", player.Id));
        Assert.IsFalse(Database.GetAcceptedChallenge(gameId)!.Game.Board.IsEndGame);
        await Act(id, host, "kick", player.Id, confirm: true);
        Assert.IsTrue(game.Game.Board.IsEndGame); Assert.AreEqual("*", game.Result);
        Assert.IsTrue(game.Interventions.Contains("host-interruption")); Assert.IsNull(Rooms.Find(id)!.CurrentGameId);
        Assert.IsTrue(await GetService<GameHistoryService>().BelongsToAsync(game.HistoryId, host.Id));
    }

    [TestMethod]
    public async Task EmptyRoomCancelsComputerAndPreservesOrganizerHistory()
    {
        var host = Human("Organizer"); var id = await Rooms.CreateAsync(host);
        await Act(id, host, "move", host.Id, "observer"); await AddEngine(id, host, "white"); await AddEngine(id, host, "black"); await Act(id, host, "start");
        var gameId = Rooms.Find(id)!.CurrentGameId!.Value;
        var game = Database.GetAcceptedChallenge(gameId)!;
        using var search = new CancellationTokenSource(); game.Game.SearchCancellation = search;
        await Act(id, host, "leave");
        Assert.IsTrue(search.IsCancellationRequested); Assert.IsNull(Rooms.Find(id)); Assert.AreEqual(0, Database.GetGames().Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => GetService<GameSessionService>().StoreAsync(gameId, game));
        var saved = await GetService<TemplateDbContext>().CompletedGames.SingleAsync(g => g.Id == game.HistoryId);
        Assert.AreEqual(host.Id.ToString(), saved.OrganizerUserId); Assert.IsNotNull(saved.MetadataJson); Assert.AreEqual("*", saved.Result);
    }

    [TestMethod]
    public async Task SnapshotKeepsRoomAndPerSideConfigurationAndRejectsStaleCommand()
    {
        var host = Human("Host"); var id = await Rooms.CreateAsync(host); await AddEngine(id, host, "black");
        var room = Rooms.Find(id)!; var version = room.Version;
        var restored = (ManagedRoom)JsonSerializer.Deserialize<RoomSnapshot>(JsonSerializer.Serialize(RoomSnapshot.Capture(room)))!.Restore();
        Assert.AreEqual(host.Id, restored.Creator.Id); Assert.AreEqual("engine", restored.Black!.Kind); Assert.AreEqual(room.Version, restored.Version);
        await Act(id, host, "swap");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rooms.ExecuteAsync(id, host, new RoomCommand { CommandId = Guid.NewGuid(), Version = version, Kind = "start" }));
        Assert.IsNull(Rooms.Find(id)!.CurrentGameId);
    }

    [TestMethod]
    public async Task ExpiredPresenceTransfersHostAndEventuallyRemovesTheRoom()
    {
        var host = Human("Host"); var viewer = Human("Viewer"); var id = await Rooms.CreateAsync(host); await Rooms.JoinAsync(id, viewer);
        var room = Rooms.Find(id)!; room.Members.Single(m => m.Player.Id == host.Id).SeenAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await Rooms.TickAsync(); Assert.AreEqual(viewer.Id, room.Creator.Id);
        room.Members.Single().SeenAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await Rooms.TickAsync(); Assert.IsNull(Rooms.Find(id));
    }

    [TestMethod]
    public async Task TwoEnginesAdvanceWithoutAnyBrowserRequest()
    {
        var host = Human("Organizer"); var id = await Rooms.CreateAsync(host); await Act(id, host, "move", host.Id, "observer");
        await AddEngine(id, host, "white"); await AddEngine(id, host, "black"); await Act(id, host, "start");
        var gameId = Rooms.Find(id)!.CurrentGameId!.Value;
        GetService<ComputerTurnCoordinator>().Wake(gameId);
        var game = Database.GetAcceptedChallenge(gameId)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (game.Game.Board.MovesToSan.Count < 4) await Task.Delay(25, deadline.Token);
        Assert.IsTrue(game.AiTurns.Count >= 4); Assert.AreEqual("engine", game.WhiteController!.Kind);
        Assert.IsTrue(game.AiTurns.All(turn => turn.Milliseconds >= 2000));
        await Act(id, host, "leave");
    }
    [TestMethod]
    public async Task NewRoomPageAndEmbeddedBoardRenderAndLegacyAcceptCannotStartIt()
    {
        await RegisterAndLoginAsync();
        var created = await PostForm("/rooms/create", new(), tokenUrl: "/Lobby");
        Assert.AreEqual(System.Net.HttpStatusCode.Found, created.StatusCode);
        var id = int.Parse(created.Headers.Location!.OriginalString.Split('/').Last());
        var page = await Http.GetAsync($"/rooms/{id}"); Assert.AreEqual(System.Net.HttpStatusCode.OK, page.StatusCode);
        StringAssert.Contains(await page.Content.ReadAsStringAsync(), "room-settings");
        var accepted = await PostForm($"/challenge/accept/{id}", new(), tokenUrl: "/Lobby");
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, accepted.StatusCode);
        var host = Rooms.Find(id)!.Creator;
        await AddEngine(id, host, "black"); await Act(id, host, "start");
        page = await Http.GetAsync($"/rooms/{id}"); Assert.AreEqual(System.Net.HttpStatusCode.OK, page.StatusCode);
        StringAssert.Contains(await page.Content.ReadAsStringAsync(), "id=\"board\"");
    }

    [TestMethod]
    public async Task RepeatedStartCommandCannotCreateTwoGames()
    {
        var host = Human("Host"); var id = await Rooms.CreateAsync(host); await AddEngine(id, host, "black");
        var command = new RoomCommand { CommandId = Guid.NewGuid(), Version = Rooms.Find(id)!.Version, Kind = "start" };
        await Rooms.ExecuteAsync(id, host, command); await Rooms.ExecuteAsync(id, host, command);
        Assert.AreEqual(1, Database.GetGames().Length);
    }

    [TestMethod]
    public async Task GuestSignInKeepsRoomMembershipAndFrozenPlayerIdentity()
    {
        var guest = Human("Guest"); guest.IsGuest = true; var id = await Rooms.CreateAsync(guest);
        await AddEngine(id, guest, "black"); await Act(id, guest, "start");
        var signedIn = Human("Signed in");
        await GetService<GameSessionService>().ClaimGuestAsync(guest.Id, signedIn);
        var room = Rooms.Find(id)!; var game = Database.GetAcceptedChallenge(room.CurrentGameId!.Value)!;
        Assert.AreEqual(signedIn.Id, room.Creator.Id); Assert.AreEqual(signedIn.Id, room.Members.Single().Player.Id);
        Assert.AreEqual(signedIn.Id, game.GetWhitePlayer().Id); Assert.AreEqual(signedIn.Id, game.WhiteController!.Player.Id);
        Assert.AreEqual(signedIn.Id, game.Organizer!.Id);
    }

    [TestMethod]
    public async Task NextGameReusesInvitationButPreservesThePreviousMatch()
    {
        var host = Human("Host"); var other = Human("Other"); var id = await Rooms.CreateAsync(host);
        await Rooms.JoinAsync(id, other); await Act(id, host, "move", other.Id, "black"); await Act(id, host, "start");
        var firstId = Rooms.Find(id)!.CurrentGameId!.Value; var first = Database.GetAcceptedChallenge(firstId)!;
        await GetService<GameSessionService>().ExecuteAsync(firstId, host.Id, new GameCommand(Guid.NewGuid(), "resign"));
        var pgn = first.Pgn();
        await Act(id, host, "next"); await Act(id, host, "swap"); await Act(id, host, "start");
        var nextId = Rooms.Find(id)!.CurrentGameId!.Value;
        Assert.AreNotEqual(firstId, nextId); Assert.AreEqual(pgn, first.Pgn());
        Assert.AreEqual(host.Id, first.GetWhitePlayer().Id); Assert.AreEqual(other.Id, Database.GetAcceptedChallenge(nextId)!.GetWhitePlayer().Id);
        Assert.AreEqual(id, Database.GetAcceptedChallenge(nextId)!.ParentRoomId);
    }

    [TestMethod]
    public async Task QueuedJoinCannotResurrectARoomAfterTheLastHumanLeaves()
    {
        var host = Human("Host"); var id = await Rooms.CreateAsync(host); var room = Rooms.Find(id)!;
        await room.MutationLock.WaitAsync();
        var leave = Rooms.ExecuteAsync(id, host, new RoomCommand { CommandId = Guid.NewGuid(), Version = room.Version, Kind = "leave" });
        var join = Rooms.JoinAsync(id, Human("Too late"));
        room.MutationLock.Release();
        await leave;
        await Assert.ThrowsAsync<InvalidOperationException>(() => join);
        Assert.IsNull(Rooms.Find(id));
        Assert.IsNull((await GetService<TemplateDbContext>().GameSessions.FindAsync(id))!.State);
    }

}
