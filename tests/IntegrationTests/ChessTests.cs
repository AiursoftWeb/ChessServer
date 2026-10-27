using Aiursoft.AiurObserver;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Entities;
using Aiursoft.ChessServer.Services.BackgroundJobs;
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
            ["Message"] = "A real challenge",
            ["RoleRule"] = "0",
            ["Permission"] = "1",
            ["CreatorId"] = Guid.Empty.ToString(), ["Minutes"] = "3"
        });
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var id = int.Parse(response.Headers.Location!.OriginalString.Split('/').Last());
        var room = GetService<InMemoryDatabase>().GetChallenge(id)!;
        Assert.AreNotEqual(Guid.Empty, room.Creator.Id);
        Assert.AreEqual(TimeSpan.FromMinutes(3), room.TimeLimit);
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
    public void RepeatedPositionFinishesTheGameInsteadOfKeepingItRunningForever()
    {
        var game = new Game();
        foreach (var move in new[] { "Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1", "Ng8" }) game.Board.Move(move);
        Assert.IsTrue(game.Board.IsEndGame);
    }

    [TestMethod]
    public async Task CreatingComputerGameDoesNotStartAnInternalWebSocket()
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/pve/new", new Dictionary<string, string> { ["difficulty"] = "1" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var room = GetService<InMemoryDatabase>().GetOnGoingOpenChallenges().Single().Value as AcceptedChallenge;
        Assert.IsNotNull(room);
        Assert.AreEqual(0, room.Game.FenChangedChannel.GetListenerCount());
        Assert.IsFalse(room.Game.IsThinking);
    }

    [TestMethod]
    public async Task ExpiringAnActiveRoomTerminatesItsRealWebSocket()
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/pve/new", new Dictionary<string, string> { ["difficulty"] = "1" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var database = GetService<InMemoryDatabase>();
        var pair = database.GetOnGoingOpenChallenges().Single();
        using var socket = new ClientWebSocket();
        socket.Options.Cookies = Cookies;
        socket.Options.SetRequestHeader("Origin", Http.BaseAddress!.GetLeftPart(UriPartial.Authority));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new UriBuilder(Http.BaseAddress) { Scheme = "ws", Path = $"/games/{pair.Key}.ws" }.Uri, deadline.Token);
        var buffer = new byte[4096];
        await socket.ReceiveAsync(buffer, deadline.Token);
        pair.Value.LastActivity = DateTimeOffset.UtcNow.AddHours(-1);
        Assert.AreEqual(1, database.RemoveExpired(DateTimeOffset.UtcNow));
        try
        {
            var result = await socket.ReceiveAsync(buffer, deadline.Token);
            Assert.AreEqual(WebSocketMessageType.Close, result.MessageType);
        }
        catch (WebSocketException) { /* Expiration aborts the receive loop. */ }
        Assert.IsFalse(deadline.IsCancellationRequested);
        Assert.IsNull(database.GetChallenge(pair.Key));
        Assert.AreEqual(0, ((AcceptedChallenge)pair.Value).Game.FenChangedChannel.GetListenerCount());
    }

    [TestMethod]
    public async Task ComputerGameCanStartWhileThePlayerWaitsForAFriend()
    {
        await RegisterAndLoginAsync();
        var room = await PostForm("/challenge/create", new Dictionary<string, string> { ["Message"] = "Waiting for a friend" });
        Assert.AreEqual(HttpStatusCode.Found, room.StatusCode);
        var game = await PostForm("/pve/new", new Dictionary<string, string> { ["difficulty"] = "1" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Found, game.StatusCode);
        Assert.AreEqual(1, GetService<InMemoryDatabase>().GetPublicOpenChallenges().Count);
        Assert.AreEqual(1, GetService<InMemoryDatabase>().GetOnGoingOpenChallenges().Count);
    }

    [TestMethod]
    public async Task CancelledEngineSearchDoesNotKeepWorking()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => GetService<ChessEngine>().GetBestMoveAsync(new ChessBoard().ToFen(), 8, cancellation.Token));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(5)]
    [DataRow(10)]
    [DataRow(15)]
    public async Task ComputerGameAcceptsSupportedClocksAndBlackPieces(int minutes)
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/pve/new", new Dictionary<string, string>
        { ["difficulty"] = "1", ["roleRule"] = "1", ["minutes"] = minutes.ToString() }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var room = (AcceptedChallenge)GetService<InMemoryDatabase>().GetOnGoingOpenChallenges().Single().Value;
        Assert.AreEqual(room.Creator.Id, room.GetBlackPlayer().Id);
        Assert.AreEqual(minutes != 0, room.IsTimed);
        Assert.AreEqual(minutes == 0 ? TimeSpan.MaxValue : TimeSpan.FromMinutes(minutes), room.TimeLimit);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(1)]
    [DataRow(30)]
    public async Task UnsupportedClockCannotCreateComputerOrFriendGame(int minutes)
    {
        await RegisterAndLoginAsync();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await PostForm("/pve/new", new Dictionary<string, string>
        { ["minutes"] = minutes.ToString() }, tokenUrl: "/Lobby")).StatusCode);
        var response = await PostForm("/challenge/create", new Dictionary<string, string> { ["Minutes"] = minutes.ToString() });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(0, GetService<InMemoryDatabase>().GetPublicOpenChallenges().Count);
    }

    [TestMethod]
    public async Task OnlyCurrentSideLosesTimeAndZeroEndsTheGameAndHidesItFromLobby()
    {
        var database = GetService<InMemoryDatabase>();
        var id = database.CreateChallenge(new Challenge(new Player(Guid.NewGuid()), "Clock test", RoleRule.CreatorWhite,
            TimeSpan.FromMinutes(3), ChallengePermission.Public));
        await database.AcceptAsync(id, new Player(Guid.NewGuid()));
        var room = database.GetAcceptedChallenge(id)!;
        var start = room.LastClockUpdate;
        room.UpdateClock(start.AddSeconds(12));
        Assert.AreEqual(TimeSpan.FromSeconds(168), room.WhiteRemaining);
        Assert.AreEqual(TimeSpan.FromMinutes(3), room.BlackRemaining);
        room.Game.Board.Move("e4");
        room.UpdateClock(start.AddSeconds(25));
        Assert.AreEqual(TimeSpan.FromSeconds(167), room.BlackRemaining);
        room.UpdateClock(start.AddSeconds(192));
        Assert.IsTrue(room.Game.Board.IsEndGame);
        Assert.AreEqual(EndgameType.Timeout, room.Game.Board.EndGame!.EndgameType);
        Assert.AreEqual("1-0", room.Result);
        Assert.AreEqual(TimeSpan.Zero, room.BlackRemaining);
        Assert.AreEqual(0, database.GetOnGoingOpenChallenges().Count);
        room.UpdateClock(start.AddMinutes(20));
        Assert.AreEqual(TimeSpan.FromSeconds(168), room.WhiteRemaining);
        Assert.AreEqual(TimeSpan.Zero, room.BlackRemaining);
    }

    [TestMethod]
    public async Task UnlimitedGameNeverTimesOutAndRefreshingCannotResetTimedClock()
    {
        await RegisterAndLoginAsync();
        await PostForm("/pve/new", new Dictionary<string, string> { ["minutes"] = "3" }, tokenUrl: "/Lobby");
        var database = GetService<InMemoryDatabase>();
        var pair = database.GetOnGoingOpenChallenges().Single();
        var room = (AcceptedChallenge)pair.Value;
        room.UpdateClock(room.LastClockUpdate.AddSeconds(30));
        var remaining = room.WhiteRemaining;
        await Http.GetAsync($"/games/{pair.Key}.html");
        await Http.GetAsync($"/games/{pair.Key}.json");
        Assert.AreEqual(remaining, room.WhiteRemaining);
        var unlimitedId = database.CreateChallenge(new Challenge(new Player(Guid.NewGuid()), "Unlimited", RoleRule.Random, TimeSpan.MaxValue, ChallengePermission.Public));
        await database.AcceptAsync(unlimitedId, new Player(Guid.NewGuid()));
        var unlimited = database.GetAcceptedChallenge(unlimitedId)!;
        unlimited.UpdateClock(unlimited.LastClockUpdate.AddDays(30));
        Assert.IsFalse(unlimited.Game.Board.IsEndGame);
    }

    [TestMethod]
    public async Task CompletedPgnIsValidatedSavedOnceAndOnlyParticipantsCanReplayIt()
    {
        await RegisterAndLoginAsync();
        await PostForm("/pve/new", new Dictionary<string, string>(), tokenUrl: "/Lobby");
        var pair = GetService<InMemoryDatabase>().GetOnGoingOpenChallenges().Single();
        var room = (AcceptedChallenge)pair.Value;
        var premature = await PostForm($"/games/{pair.Key}/complete", new Dictionary<string, string> { ["pgn"] = "fake" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Conflict, premature.StatusCode);
        foreach (var move in new[] { "f3", "e5", "g4", "Qh4#" }) room.Game.Board.Move(move);
        var pgn = room.Pgn();
        Assert.AreEqual(pgn, room.Pgn());
        Assert.IsTrue(pgn.Contains("0-1", StringComparison.Ordinal));
        var forged = await PostForm($"/games/{pair.Key}/complete", new Dictionary<string, string> { ["pgn"] = "forged result" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await PostForm($"/games/{pair.Key}/complete", [], tokenUrl: "/Lobby")).StatusCode);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var saved = await PostForm($"/games/{pair.Key}/complete", new Dictionary<string, string> { ["pgn"] = pgn.Replace("\r\n", "\n").Replace("\n", "\r\n") }, tokenUrl: "/Lobby");
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        }
        var context = GetService<TemplateDbContext>();
        Assert.AreEqual(1, context.CompletedGames.Count(g => g.Id == room.HistoryId));
        Assert.IsTrue((await (await Http.GetAsync("/History")).Content.ReadAsStringAsync()).Contains("0-1", StringComparison.Ordinal));
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/History/Replay/{room.HistoryId}")).StatusCode);
        var exported = await (await Http.GetAsync($"/History/Pgn/{room.HistoryId}")).Content.ReadAsStringAsync();
        Assert.AreEqual(pgn, exported);
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var old = await (await Http.GetAsync($"/History?date={yesterday}")).Content.ReadAsStringAsync();
        Assert.IsTrue(old.Contains("No completed games on this date.", StringComparison.Ordinal));
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/History/Replay/{room.HistoryId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/History/Pgn/{room.HistoryId}")).StatusCode);
        AssertRedirect(await PostForm($"/games/{pair.Key}/complete", new Dictionary<string, string> { ["pgn"] = pgn }, tokenUrl: "/Lobby"), "/Error/Code403", exact: false);
    }

    [TestMethod]
    public async Task TimeoutWithoutConnectedPlayersIsArchivedByExistingBackgroundJob()
    {
        var database = GetService<InMemoryDatabase>();
        var id = database.CreateChallenge(new Challenge(new Player(Guid.NewGuid()), "Nobody online", RoleRule.CreatorWhite, TimeSpan.FromMinutes(3), ChallengePermission.Public));
        await database.AcceptAsync(id, new Player(Guid.NewGuid()));
        var room = database.GetAcceptedChallenge(id)!;
        room.UpdateClock(room.LastClockUpdate.AddMinutes(4));
        await GetService<ExpiredGamesCleanupJob>().ExecuteAsync();
        Assert.IsTrue(room.Archived);
        Assert.AreEqual(1, GetService<TemplateDbContext>().CompletedGames.Count(g => g.Id == room.HistoryId));
        Assert.AreEqual(0, database.GetOnGoingOpenChallenges().Count);
    }

    [TestMethod]
    public async Task ComputerStartsWithWhiteWhenHumanChoosesBlack()
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/pve/new", new Dictionary<string, string> { ["difficulty"] = "1", ["roleRule"] = "1", ["minutes"] = "3" }, tokenUrl: "/Lobby");
        var id = int.Parse(response.Headers.Location!.OriginalString.Split('/').Last().Split('.')[0]);
        using var socket = new ClientWebSocket();
        socket.Options.Cookies = Cookies;
        socket.Options.SetRequestHeader("Origin", Http.BaseAddress!.GetLeftPart(UriPartial.Authority));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(new UriBuilder(Http.BaseAddress) { Scheme = "ws", Path = $"/games/{id}.ws" }.Uri, deadline.Token);
        var buffer = new byte[4096];
        await socket.ReceiveAsync(buffer, deadline.Token);
        var reply = await socket.ReceiveAsync(buffer, deadline.Token);
        Assert.IsTrue(Encoding.UTF8.GetString(buffer, 0, reply.Count).Contains(" b ", StringComparison.Ordinal));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Finished", deadline.Token);
    }

    [TestMethod]
    public async Task FriendGameCanUseNoClockAndRandomReallyAssignsBothColors()
    {
        await RegisterAndLoginAsync();
        var response = await PostForm("/challenge/create", new Dictionary<string, string>
        { ["Minutes"] = "0", ["RoleRule"] = "2" });
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var database = GetService<InMemoryDatabase>();
        var room = database.GetPublicOpenChallenges().Single().Value;
        Assert.AreEqual(TimeSpan.MaxValue, room.TimeLimit);
        var colors = new HashSet<Guid>();
        var creator = new Player(Guid.NewGuid());
        var opponent = new Player(Guid.NewGuid());
        for (var i = 0; i < 40; i++)
        {
            var random = new AcceptedChallenge(creator, opponent, "Random pieces", RoleRule.Random,
                TimeSpan.MaxValue, ChallengePermission.Public, new AsyncObservable<string>());
            colors.Add(random.GetWhitePlayer().Id);
        }
        Assert.AreEqual(2, colors.Count);
    }

    [TestMethod]
    public async Task BothParticipantsSeeOneSharedHistoryRecordAndConcurrentSavesAreIdempotent()
    {
        await RegisterAndLoginAsync();
        await PostForm("/challenge/create", new Dictionary<string, string>());
        var database = GetService<InMemoryDatabase>();
        var waiting = database.GetPublicOpenChallenges().Single();
        await LoginAsAdmin();
        await PostForm($"/challenge/accept/{waiting.Key}", [], tokenUrl: "/Lobby");
        var room = database.GetAcceptedChallenge(waiting.Key)!;
        foreach (var move in new[] { "f3", "e5", "g4", "Qh4#" }) room.Game.Board.Move(move);
        var history = GetService<GameHistoryService>();
        await Task.WhenAll(history.SaveAsync(room), history.SaveAsync(room), history.SaveAsync(room));
        Assert.AreEqual(1, GetService<TemplateDbContext>().CompletedGames.Count(g => g.Id == room.HistoryId));
        var response = await Http.GetAsync($"/History/Replay/{room.HistoryId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var index = await (await Http.GetAsync("/History")).Content.ReadAsStringAsync();
        Assert.IsTrue(index.Contains(room.HistoryId.ToString(), StringComparison.Ordinal));
    }
}
