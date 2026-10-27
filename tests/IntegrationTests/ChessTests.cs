using Aiursoft.AiurObserver;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services;
using Chess;

namespace Aiursoft.ChessServer.Tests.IntegrationTests;

[TestClass]
public class ChessTests : TestBase
{
    [TestMethod]
    [DataRow("/Lobby")]
    [DataRow("/challenge/create")]
    [DataRow("/games/1.fen")]
    [DataRow("/games/1.ws")]
    [DataRow("/chats/1.ws")]
    [DataRow("/challenge/listen/1.ws")]
    public async Task AnonymousCannotEnterGames(string path)
    {
        var response = await Http.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        AssertRedirect(response, "/Account/Login", exact: false);
    }

    [TestMethod]
    public async Task ChallengeUsesAuthenticatedIdentityAndCannotBeDroppedByAnotherUser()
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/challenge/create", new Dictionary<string, string>
        {
            ["Message"] = "A real challenge", ["RoleRule"] = "0", ["Permission"] = "1",
            ["CreatorId"] = Guid.Empty.ToString()
        });
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var id = int.Parse(response.Headers.Location!.OriginalString.Split('/').Last());
        var room = GetService<InMemoryDatabase>().GetChallenge(id)!;
        Assert.AreNotEqual(Guid.Empty, room.Creator.Id);
        Assert.AreEqual(0, GetService<InMemoryDatabase>().GetPublicOpenChallenges().Count);
        await LoginAsAdmin();
        var drop = await PostForm($"/challenge/{id}/drop", [], tokenUrl: "/Lobby");
        AssertRedirect(drop, "/Error/Code403", exact: false);
        Assert.IsNotNull(GetService<InMemoryDatabase>().GetChallenge(id));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(9)]
    [DataRow(-1)]
    public async Task InvalidDifficultyDoesNotCreateRoom(int difficulty)
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/pve/new", new Dictionary<string, string> { ["difficulty"] = difficulty.ToString() }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, GetService<InMemoryDatabase>().GetOnGoingOpenChallenges().Count);
    }

    [TestMethod]
    public async Task CreateRequiresAntiforgeryToken()
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/pve/new", new Dictionary<string, string> { ["difficulty"] = "1" }, includeToken: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ExpiredGameCancelsConnectionsAndRemovesSubscriptions()
    {
        var database = GetService<InMemoryDatabase>();
        var id = database.CreateChallenge(new Challenge(new Player(Guid.NewGuid()), "Expired game", RoleRule.CreatorWhite,
            TimeSpan.FromMinutes(10), ChallengePermission.Public));
        await database.AcceptAsync(id, new Player(Guid.NewGuid()));
        var room = database.GetAcceptedChallenge(id)!;
        var lifetime = room.Lifetime;
        var subscription = room.Game.FenChangedChannel.Subscribe(_ => Task.CompletedTask);
        room.LastActivity = DateTimeOffset.UtcNow.AddHours(-1);
        Assert.AreEqual(1, database.RemoveExpired(DateTimeOffset.UtcNow));
        Assert.IsTrue(lifetime.IsCancellationRequested);
        Assert.IsNull(database.GetChallenge(id));
        Assert.AreEqual(0, room.Game.FenChangedChannel.GetListenerCount());
        subscription.Unsubscribe();
    }

    [TestMethod]
    public async Task AcceptPreservesWaitingConnectionLifetime()
    {
        var database = GetService<InMemoryDatabase>();
        var id = database.CreateChallenge(new Challenge(new Player(Guid.NewGuid()), "Waiting room", RoleRule.CreatorWhite,
            TimeSpan.FromMinutes(10), ChallengePermission.Public));
        var lifetime = database.GetChallenge(id)!.Lifetime;
        await database.AcceptAsync(id, new Player(Guid.NewGuid()));
        database.DeleteChallenge(id);
        Assert.IsTrue(lifetime.IsCancellationRequested);
    }

    [TestMethod]
    public async Task ConcurrentAcceptHasOneWinner()
    {
        var database = GetService<InMemoryDatabase>();
        var id = database.CreateChallenge(new Challenge(new Player(Guid.NewGuid()), "One opponent", RoleRule.CreatorWhite,
            TimeSpan.FromMinutes(10), ChallengePermission.Public));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try { await database.AcceptAsync(id, new Player(Guid.NewGuid())); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.AreEqual(1, attempts.Count(success => success));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(4)]
    [DataRow(8)]
    public async Task EngineProducesLegalMoveWithinItsBudget(int difficulty)
    {
        var board = new ChessBoard();
        board.Move("e4");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var move = await GetService<ChessEngine>().GetBestMoveAsync(board.ToFen(), difficulty, deadline.Token);
        Assert.IsTrue(board.IsValidMove(move), move);
    }

    [TestMethod]
    public async Task RealWebSocketPlaysAgainstComputerAndUnsubscribesOnClose()
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/pve/new", new Dictionary<string, string> { ["difficulty"] = "1" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var id = int.Parse(response.Headers.Location!.OriginalString.Split('/').Last().Split('.')[0]);
        using var socket = new ClientWebSocket();
        socket.Options.Cookies = Cookies;
        socket.Options.SetRequestHeader("Origin", Http.BaseAddress!.GetLeftPart(UriPartial.Authority));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var uri = new UriBuilder(Http.BaseAddress) { Scheme = "ws", Path = $"/games/{id}.ws" }.Uri;
        await socket.ConnectAsync(uri, deadline.Token);
        var buffer = new byte[4096];
        var initial = await socket.ReceiveAsync(buffer, deadline.Token);
        Assert.IsTrue(Encoding.UTF8.GetString(buffer, 0, initial.Count).Contains(" w ", StringComparison.Ordinal));
        await socket.SendAsync(Encoding.UTF8.GetBytes("e4"), WebSocketMessageType.Text, true, deadline.Token);
        var moved = await socket.ReceiveAsync(buffer, deadline.Token);
        Assert.IsTrue(Encoding.UTF8.GetString(buffer, 0, moved.Count).Contains(" b ", StringComparison.Ordinal));
        var reply = await socket.ReceiveAsync(buffer, deadline.Token);
        Assert.IsTrue(Encoding.UTF8.GetString(buffer, 0, reply.Count).Contains(" w ", StringComparison.Ordinal));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Finished", deadline.Token);
        var room = GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!;
        for (var attempt = 0; attempt < 20 && room.Game.FenChangedChannel.GetListenerCount() > 0; attempt++) await Task.Delay(25, deadline.Token);
        Assert.AreEqual(0, room.Game.FenChangedChannel.GetListenerCount());
        Assert.IsFalse(room.Game.IsThinking);
    }

    [TestMethod]
    public async Task BeginnerChoosesVariedLegalMovesRatherThanAlwaysPlayingTheEngineBestMove()
    {
        using var engine = new ChessEngine(new Random(42));
        var board = new ChessBoard();
        board.Move("e4");
        var moves = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            var move = await engine.GetBestMoveAsync(board.ToFen(), 1, CancellationToken.None);
            Assert.IsTrue(board.IsValidMove(move), move);
            moves.Add(move);
        }
        Assert.IsGreaterThan(5, moves.Count);
    }

    [TestMethod]
    public async Task CancelledEngineSearchDoesNotKeepWorking()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => GetService<ChessEngine>().GetBestMoveAsync(new ChessBoard().ToFen(), 8, cancellation.Token));
    }
}
