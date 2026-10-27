using Aiursoft.AiurObserver;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        room.Game.Board.Resign(PieceColor.White);
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
        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new GameCommand(Guid.NewGuid(), "move", 0, "e4"))), WebSocketMessageType.Text, true, deadline.Token);
        var moved = await socket.ReceiveAsync(buffer, deadline.Token);
        Assert.IsTrue(Encoding.UTF8.GetString(buffer, 0, moved.Count).Contains(" b ", StringComparison.Ordinal));
        var reply = await socket.ReceiveAsync(buffer, deadline.Token);
        while (!Encoding.UTF8.GetString(buffer, 0, reply.Count).Contains(" w ", StringComparison.Ordinal)) reply = await socket.ReceiveAsync(buffer, deadline.Token);
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
        ((AcceptedChallenge)pair.Value).Game.Board.Resign(PieceColor.White);
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

    [TestMethod]
    [DataRow("resign", "0-1", "Resigned")]
    [DataRow("abort", "*", "Aborted")]
    public async Task PlayersCanEndGamesAndTheSameCommandIsNeverExecutedTwice(string action, string result, string reason)
    {
        await RegisterAndLoginAsync();
        await PostForm("/pve/new", [], tokenUrl: "/Lobby");
        var pair = GetService<InMemoryDatabase>().GetOnGoingOpenChallenges().Single();
        var room = (AcceptedChallenge)pair.Value;
        var id = Guid.NewGuid();
        for (var i = 0; i < 2; i++)
        {
            var response = await PostForm($"/games/{pair.Key}/action", new Dictionary<string, string> { ["kind"] = action, ["commandId"] = id.ToString() }, tokenUrl: "/Lobby");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.IsTrue(room.Game.Board.IsEndGame);
        Assert.AreEqual(result, room.Result); Assert.AreEqual(reason, room.OutcomeReason);
        Assert.AreEqual(1, room.Receipts.Count);
        Assert.AreEqual(0, GetService<InMemoryDatabase>().GetOnGoingOpenChallenges().Count);
        var saved = GetService<TemplateDbContext>().CompletedGames.Single(g => g.Id == room.HistoryId);
        Assert.AreEqual(result, saved.Result); Assert.AreEqual(reason, saved.EndReason);
        Assert.IsTrue(saved.Pgn.EndsWith(result, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FriendDrawOfferCanBeDeclinedAndAcceptedOnlyByTheOtherPlayer()
    {
        var sessions = GetService<GameSessionService>();
        var white = new Player(Guid.NewGuid()); var black = new Player(Guid.NewGuid());
        var id = await sessions.CreateAsync(new Challenge(white, "Draw agreement", RoleRule.CreatorWhite, TimeSpan.MaxValue, ChallengePermission.Public));
        await sessions.AcceptAsync(id, black);
        Assert.IsNull((await sessions.ExecuteAsync(id, white.Id, new GameCommand(Guid.NewGuid(), "offer-draw"))).Error);
        Assert.AreEqual("no-offer", (await sessions.ExecuteAsync(id, white.Id, new GameCommand(Guid.NewGuid(), "accept-draw"))).Error);
        Assert.IsNull((await sessions.ExecuteAsync(id, black.Id, new GameCommand(Guid.NewGuid(), "decline-draw"))).Error);
        Assert.IsNull(GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!.DrawOfferedBy);
        await sessions.ExecuteAsync(id, white.Id, new GameCommand(Guid.NewGuid(), "offer-draw"));
        Assert.IsNull((await sessions.ExecuteAsync(id, black.Id, new GameCommand(Guid.NewGuid(), "accept-draw"))).Error);
        var room = GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!;
        Assert.AreEqual("1/2-1/2", room.Result); Assert.AreEqual("Agreement", room.OutcomeReason);
    }

    [TestMethod]
    public async Task StartedGamesCannotBeAbortedAndSpectatorsCannotControlGames()
    {
        var sessions = GetService<GameSessionService>(); var white = new Player(Guid.NewGuid());
        var id = await sessions.CreateAsync(new Challenge(white, "Real game", RoleRule.CreatorWhite, TimeSpan.MaxValue, ChallengePermission.Public));
        await sessions.AcceptAsync(id, new Player(Guid.NewGuid()));
        var move = new GameCommand(Guid.NewGuid(), "move", 0, "e4");
        Assert.IsNull((await sessions.ExecuteAsync(id, white.Id, move)).Error);
        Assert.AreEqual("cannot-abort", (await sessions.ExecuteAsync(id, white.Id, new GameCommand(Guid.NewGuid(), "abort"))).Error);
        Assert.AreEqual("spectator", (await sessions.ExecuteAsync(id, Guid.NewGuid(), new GameCommand(Guid.NewGuid(), "resign"))).Error);
        Assert.AreEqual("stale", (await sessions.ExecuteAsync(id, white.Id, new GameCommand(Guid.NewGuid(), "move", 0, "d4"))).Error);
        Assert.IsNull((await sessions.ExecuteAsync(id, white.Id, move)).Error);
        Assert.AreEqual(1, GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!.Game.Board.MovesToSan.Count);
    }

    [TestMethod]
    public async Task ANewProcessCanRestoreTheSameRoomMovesColorsClockAndCommandReceipts()
    {
        var sessions = GetService<GameSessionService>(); var white = new Player(Guid.NewGuid()); var black = new Player(Guid.NewGuid());
        var id = await sessions.CreateAsync(new Challenge(white, "Survive restart", RoleRule.Random, TimeSpan.FromMinutes(5), ChallengePermission.Public));
        await sessions.AcceptAsync(id, black);
        var room = GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!;
        var first = new GameCommand(Guid.NewGuid(), "move", 0, "e4");
        await sessions.ExecuteAsync(id, room.GetWhitePlayer().Id, first);
        await sessions.ExecuteAsync(id, room.GetBlackPlayer().Id, new GameCommand(Guid.NewGuid(), "move", 1, "e5"));
        var replacement = new InMemoryDatabase();
        var recovered = new GameSessionService(replacement, GetService<TemplateDbContext>(), GetService<GameHistoryService>(), NullLogger<GameSessionService>.Instance);
        await recovered.RestoreAsync();
        var restored = replacement.GetAcceptedChallenge(id)!;
        Assert.AreEqual(room.HistoryId, restored.HistoryId); Assert.AreEqual(room.GetWhitePlayer().Id, restored.GetWhitePlayer().Id);
        Assert.AreEqual(room.Game.Board.ToFen(), restored.Game.Board.ToFen());
        Assert.IsTrue(restored.WhiteRemaining <= TimeSpan.FromMinutes(5));
        Assert.AreEqual(room.Revision, restored.Revision);
        Assert.IsNull((await recovered.ExecuteAsync(id, restored.GetWhitePlayer().Id, first)).Error);
        Assert.AreEqual(2, restored.Game.Board.MovesToSan.Count);
        var next = await recovered.CreateAsync(new Challenge(new Player(Guid.NewGuid()), "Next room", RoleRule.Random, TimeSpan.MaxValue, ChallengePermission.Public));
        Assert.IsGreaterThan(id, next);
    }

    [TestMethod]
    [DataRow("8/8/8/8/8/8/4k3/R5K1 w - - 0 1", 'b', false)]
    [DataRow("8/8/8/8/8/8/4k3/R5K1 w - - 0 1", 'w', true)]
    [DataRow("8/8/8/8/8/8/4k3/N5K1 w - - 0 1", 'w', false)]
    [DataRow("8/8/8/8/8/8/4k3/NN4K1 w - - 0 1", 'w', true)]
    [DataRow("8/8/8/8/8/8/4k3/BB4K1 w - - 0 1", 'w', true)]
    [DataRow("8/8/8/8/8/8/4k3/B1B3K1 w - - 0 1", 'w', false)]
    [DataRow("8/8/8/8/8/8/3nk3/N5K1 w - - 0 1", 'w', true)]
    public void TimeoutMaterialCountsPossibleMateRatherThanForcedMate(string fen, char side, bool expected)
    {
        Assert.AreEqual(expected, MatingMaterial.CanMate(fen, side));
    }

    [TestMethod]
    public async Task UnplayedGamesAbortButDisconnectedPlayersHaveAReconnectionGrace()
    {
        var sessions = GetService<GameSessionService>(); var white = new Player(Guid.NewGuid()); var black = new Player(Guid.NewGuid());
        var id = await sessions.CreateAsync(new Challenge(white, "Nobody starts", RoleRule.CreatorWhite, TimeSpan.MaxValue, ChallengePermission.Public));
        await sessions.AcceptAsync(id, black);
        var room = GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!;
        room.AbortAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await sessions.TickAsync();
        Assert.AreEqual("Aborted", room.OutcomeReason); Assert.IsTrue(room.Archived);
        var second = await sessions.CreateAsync(new Challenge(white, "Disconnected opponent", RoleRule.CreatorWhite, TimeSpan.MaxValue, ChallengePermission.Public));
        await sessions.AcceptAsync(second, black);
        await sessions.ExecuteAsync(second, white.Id, new GameCommand(Guid.NewGuid(), "move", 0, "e4"));
        Assert.AreEqual("cannot-claim", (await sessions.ExecuteAsync(second, white.Id, new GameCommand(Guid.NewGuid(), "claim"))).Error);
        var running = GetService<InMemoryDatabase>().GetAcceptedChallenge(second)!;
        running.BlackSeenAt = DateTimeOffset.UtcNow.AddSeconds(-91);
        Assert.IsNull((await sessions.ExecuteAsync(second, white.Id, new GameCommand(Guid.NewGuid(), "claim"))).Error);
        Assert.AreEqual("1-0", running.Result); Assert.AreEqual("Abandonment", running.OutcomeReason);
    }

    [TestMethod]
    public async Task CurrentGamesIncludeUnlistedGamesOwnedByTheViewer()
    {
        await RegisterAndLoginAsync();
        await PostForm("/pve/new", [], tokenUrl: "/Lobby");
        var pair = GetService<InMemoryDatabase>().GetOnGoingOpenChallenges().Single();
        pair.Value.Permission = ChallengePermission.Unlisted;
        var page = await (await Http.GetAsync("/Lobby")).Content.ReadAsStringAsync();
        Assert.IsTrue(page.Contains("Your current games", StringComparison.Ordinal));
        Assert.IsTrue(page.Contains($"/games/{pair.Key}.html", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AbandonedGamesFinishAndRemainInHistory(bool computer)
    {
        var sessions = GetService<GameSessionService>();
        var creator = new Player(Guid.NewGuid());
        var id = await sessions.CreateAsync(new Challenge(creator, "Abandoned game", RoleRule.CreatorWhite,
            TimeSpan.MaxValue, ChallengePermission.Unlisted) { Difficulty = computer ? 1 : null });
        await sessions.AcceptAsync(id, new Player(Guid.NewGuid()));
        await sessions.ExecuteAsync(id, creator.Id, new GameCommand(Guid.NewGuid(), "move", 0, "e4"));
        var room = GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!;
        room.WhiteSeenAt = DateTimeOffset.UtcNow.AddMinutes(-11);
        room.BlackSeenAt = DateTimeOffset.UtcNow.AddMinutes(-11);
        await sessions.TickAsync();
        Assert.IsTrue(room.Game.Board.IsEndGame);
        Assert.AreEqual(computer ? "Abandonment" : "Abandoned", room.OutcomeReason);
        Assert.AreEqual(computer ? "0-1" : "*", room.Result);
        var record = GetService<TemplateDbContext>().CompletedGames.Single(g => g.Id == room.HistoryId);
        Assert.AreEqual(room.Result, record.Result);
        Assert.AreEqual(room.OutcomeReason, record.EndReason);
    }

    private sealed class FailingChessContext(DbContextOptions options) : TemplateDbContext(options)
    {
        public bool FailNextSave { get; set; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextSave) { FailNextSave = false; throw new IOException("Simulated database outage"); }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    [TestMethod]
    public async Task AFailedDatabaseCommitCannotAcknowledgeOrKeepAnUncommittedMove()
    {
        await using var context = new FailingChessContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await context.Database.EnsureCreatedAsync();
        var database = new InMemoryDatabase();
        var sessions = new GameSessionService(database, context, new GameHistoryService(context), NullLogger<GameSessionService>.Instance);
        var white = new Player(Guid.NewGuid()); var black = new Player(Guid.NewGuid());
        var id = await sessions.CreateAsync(new Challenge(white, "Commit failure", RoleRule.CreatorWhite, TimeSpan.MaxValue, ChallengePermission.Public));
        await sessions.AcceptAsync(id, black);
        var command = new GameCommand(Guid.NewGuid(), "move", 0, "e4");
        context.FailNextSave = true;
        await Assert.ThrowsAsync<IOException>(() => sessions.ExecuteAsync(id, white.Id, command));
        Assert.AreEqual(0, database.GetAcceptedChallenge(id)!.Game.Board.MovesToSan.Count);
        Assert.AreEqual(0, database.GetAcceptedChallenge(id)!.Receipts.Count);
        Assert.IsNull((await sessions.ExecuteAsync(id, white.Id, command)).Error);
        Assert.AreEqual(1, database.GetAcceptedChallenge(id)!.Game.Board.MovesToSan.Count);
        var resign = new GameCommand(Guid.NewGuid(), "resign");
        context.FailNextSave = true;
        await Assert.ThrowsAsync<IOException>(() => sessions.ExecuteAsync(id, white.Id, resign));
        Assert.IsFalse(database.GetAcceptedChallenge(id)!.Game.Board.IsEndGame);
        Assert.IsFalse(database.GetAcceptedChallenge(id)!.Archived);
        Assert.AreEqual(0, await context.CompletedGames.CountAsync());
        Assert.IsNull((await sessions.ExecuteAsync(id, white.Id, resign)).Error);
        Assert.AreEqual(1, await context.CompletedGames.CountAsync());
    }

    [TestMethod]
    public void TimeoutAgainstABareKingIsADrawRatherThanAnImpossibleWin()
    {
        var room = new AcceptedChallenge(new Player(Guid.NewGuid()), new Player(Guid.NewGuid()), "Timeout material", RoleRule.CreatorWhite,
            TimeSpan.FromMinutes(3), ChallengePermission.Public, new AsyncObservable<string>());
        room.Game.Board = ChessBoard.LoadFromFen("8/8/8/8/8/8/4k3/R5K1 w - - 0 1", AutoEndgameRules.All & ~AutoEndgameRules.InsufficientMaterial);
        room.UpdateClock(room.LastClockUpdate.AddMinutes(4));
        Assert.AreEqual("1/2-1/2", room.Result);
        Assert.AreEqual("TimeoutDraw", room.OutcomeReason);
    }

    [TestMethod]
    public void TwoKnightsMustNotBeAutomaticallyDrawnBeforeAValidTimeout()
    {
        var room = new AcceptedChallenge(new Player(Guid.NewGuid()), new Player(Guid.NewGuid()), "Two knights", RoleRule.CreatorWhite,
            TimeSpan.FromMinutes(3), ChallengePermission.Public, new AsyncObservable<string>());
        room.Game.Board = ChessBoard.LoadFromFen("8/8/8/8/8/8/4k3/NN4K1 b - - 0 1", AutoEndgameRules.All & ~AutoEndgameRules.InsufficientMaterial);
        room.UpdateClock(room.LastClockUpdate.AddMinutes(4));
        Assert.AreEqual("1-0", room.Result);
        Assert.AreEqual("Timeout", room.OutcomeReason);
    }

    [TestMethod]
    public async Task LosingConcurrentAcceptCannotRestoreTheOldWaitingRoomOverTheWinner()
    {
        var sessions = GetService<GameSessionService>();
        var id = await sessions.CreateAsync(new Challenge(new Player(Guid.NewGuid()), "One persistent winner", RoleRule.Random,
            TimeSpan.FromMinutes(5), ChallengePermission.Public));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try { await sessions.AcceptAsync(id, new Player(Guid.NewGuid())); return true; }
            catch (InvalidOperationException) { return false; }
        }));
        Assert.AreEqual(1, attempts.Count(success => success));
        Assert.IsNotNull(GetService<InMemoryDatabase>().GetAcceptedChallenge(id));
        var saved = GetService<TemplateDbContext>().GameSessions.Single(g => g.Id == id);
        Assert.IsNotNull(JsonSerializer.Deserialize<RoomSnapshot>(saved.State!)!.AccepterId);
    }

    [TestMethod]
    public async Task ShutdownDrainDoesNotChargeThePlayersClock()
    {
        var sessions = GetService<GameSessionService>();
        var database = GetService<InMemoryDatabase>();
        var id = await sessions.CreateAsync(new Challenge(new Player(Guid.NewGuid()), "Shutdown drain", RoleRule.CreatorWhite,
            TimeSpan.FromMinutes(5), ChallengePermission.Public));
        await sessions.AcceptAsync(id, new Player(Guid.NewGuid()));
        database.PauseClocks();
        await sessions.CheckpointAsync();
        var before = database.GetAcceptedChallenge(id)!.WhiteRemaining;
        await Task.Delay(100);
        await sessions.TickAsync();
        await sessions.CheckpointAsync();
        Assert.AreEqual(before, database.GetAcceptedChallenge(id)!.WhiteRemaining);
    }

    [TestMethod]
    public async Task ServerMaintenanceRestoresRemainingTimeRatherThanChargingTheOutage()
    {
        var sessions = GetService<GameSessionService>(); var creator = new Player(Guid.NewGuid());
        var id = await sessions.CreateAsync(new Challenge(creator, "Maintenance clock", RoleRule.CreatorWhite, TimeSpan.FromMinutes(5), ChallengePermission.Public));
        await sessions.AcceptAsync(id, new Player(Guid.NewGuid()));
        var room = GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!;
        room.UpdateClock(room.LastClockUpdate.AddSeconds(35));
        await sessions.CheckpointAsync();
        var saved = GetService<TemplateDbContext>().GameSessions.Single(g => g.Id == id);
        var snapshot = JsonSerializer.Deserialize<RoomSnapshot>(saved.State!)!;
        snapshot.ClockAt = DateTimeOffset.UtcNow.AddHours(-1);
        saved.State = JsonSerializer.Serialize(snapshot);
        await GetService<TemplateDbContext>().SaveChangesAsync();
        var replacement = new InMemoryDatabase();
        await new GameSessionService(replacement, GetService<TemplateDbContext>(), GetService<GameHistoryService>(), NullLogger<GameSessionService>.Instance).RestoreAsync();
        var restored = replacement.GetAcceptedChallenge(id)!;
        restored.UpdateClock(DateTimeOffset.UtcNow);
        Assert.IsFalse(restored.Game.Board.IsEndGame);
        Assert.IsTrue(restored.WhiteRemaining.TotalSeconds > 264 && restored.WhiteRemaining.TotalSeconds <= 265);
    }
}
