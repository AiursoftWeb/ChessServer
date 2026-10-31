using System.Net;
using System.Text.Json;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services;

namespace Aiursoft.ChessServer.Tests.IntegrationTests;

[TestClass]
public class LlmGameTests : TestBase
{
    private WebApplication? _gateway;
    private int _completionCalls;

    private async Task ConfigureLlm(string completion = "null")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _gateway = builder.Build();
        _gateway.MapGet("/v1/models", () => Results.Json(new { data = new[] { new { id = "configured-model" }, new { id = "another-model" } } }));
        _gateway.MapPost("/v1/chat/completions", () =>
        {
            _completionCalls++;
            return Results.Text(completion, "application/json");
        });
        await _gateway.StartAsync();
        var configuration = GetService<IConfiguration>();
        configuration["OpenAI:CompletionApiUrl"] = _gateway.Urls.Single() + "/v1/chat/completions";
        configuration["OpenAI:Model"] = "configured-model";
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        await base.CleanServer();
        if (_gateway != null) await _gateway.DisposeAsync();
    }

    [TestMethod]
    public async Task ModelListShowsAllModelsAndRejectsAnUnlistedOpponent()
    {
        await ConfigureLlm(); await RegisterAndLoginAsync();
        var page = await (await Http.GetAsync("/Lobby")).Content.ReadAsStringAsync();
        StringAssert.Contains(page, "value=\"configured-model\"");
        StringAssert.Contains(page, "value=\"another-model\"");
        var response = await PostForm("/pve/new", new() { ["difficulty"] = "llm", ["model"] = "forged-model" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, GetService<InMemoryDatabase>().GetGames().Length);
    }

    [TestMethod]
    public async Task CatalogOutageDoesNotPreventEngineGames()
    {
        var configuration = GetService<IConfiguration>();
        configuration["OpenAI:CompletionApiUrl"] = "http://127.0.0.1:1/v1/chat/completions";
        await RegisterAndLoginAsync();
        var response = await PostForm("/pve/new", new() { ["difficulty"] = "1" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.AreEqual(1, GetService<InMemoryDatabase>().GetGames().Single().Difficulty);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{")]
    public async Task MalformedCompletionStopsAutomaticRetries(string completion)
    {
        await ConfigureLlm(completion); await RegisterAndLoginAsync();
        var created = await PostForm("/pve/new", new() { ["difficulty"] = "llm", ["model"] = "another-model", ["roleRule"] = "1" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Found, created.StatusCode);
        var id = GetService<InMemoryDatabase>().GetRooms().Single().Key;
        var response = await PostForm($"/games/{id}/computer", new(), tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("computer-failed", GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!.Game.ComputerError);
        await PostForm($"/games/{id}/computer", new(), tokenUrl: "/Lobby");
        Assert.AreEqual(1, _completionCalls);
    }

    [TestMethod]
    public async Task UnconfiguredLlmIsHiddenAndCannotCreateAGame()
    {
        await RegisterAndLoginAsync();
        var page = await (await Http.GetAsync("/Lobby")).Content.ReadAsStringAsync();
        Assert.IsFalse(page.Contains("value=\"llm\"", StringComparison.Ordinal));
        var response = await PostForm("/pve/new", new() { ["difficulty"] = "llm" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, GetService<InMemoryDatabase>().GetGames().Length);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("1")]
    public async Task LlmIsAnIndependentOpponentAndSurvivesSnapshotAndRematch(string pieces)
    {
        await ConfigureLlm(); await RegisterAndLoginAsync();
        var page = await (await Http.GetAsync("/Lobby")).Content.ReadAsStringAsync();
        StringAssert.Contains(page, "value=\"llm\"");
        var response = await PostForm("/pve/new", new() { ["difficulty"] = "llm", ["model"] = "another-model", ["roleRule"] = pieces, ["minutes"] = "3", ["incrementSeconds"] = "2" }, tokenUrl: "/Lobby");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var database = GetService<InMemoryDatabase>(); var room = database.GetGames().Single();
        Assert.IsTrue(room.IsComputer); Assert.IsNull(room.Difficulty);
        Assert.AreEqual("another-model", room.LlmModel);
        Assert.AreEqual("LLM — another-model", room.Accepter.NickName);
        var snapshot = JsonSerializer.Deserialize<RoomSnapshot>(JsonSerializer.Serialize(RoomSnapshot.Capture(room)))!;
        var restored = (AcceptedChallenge)snapshot.Restore();
        Assert.IsTrue(restored.IsComputer); Assert.AreEqual(room.LlmModel, restored.LlmModel);
        Assert.AreEqual(room.CreatorIsWhite, restored.CreatorIsWhite); Assert.AreEqual(2, restored.IncrementSeconds);
        var id = database.GetRooms().Single().Key; var sessions = GetService<GameSessionService>();
        await sessions.ExecuteAsync(id, room.Creator.Id, new GameCommand(Guid.NewGuid(), "resign"));
        var next = await sessions.ExecuteAsync(id, room.Creator.Id, new GameCommand(Guid.NewGuid(), "offer-rematch"));
        var rematch = database.GetAcceptedChallenge(next.State.NextGameId!.Value)!;
        Assert.AreEqual(room.LlmModel, rematch.LlmModel); Assert.IsTrue(rematch.IsComputer);
        Assert.AreNotEqual(room.CreatorIsWhite, rematch.CreatorIsWhite);
    }

    [TestMethod]
    public async Task LlmFailurePersistsButUndoClearsItAndRejectsLateResponses()
    {
        var sessions = GetService<GameSessionService>(); var player = new Player(Guid.NewGuid()) { IsGuest = true };
        var id = await sessions.CreateAsync(new Challenge(player, "LLM game", RoleRule.CreatorWhite, TimeSpan.MaxValue, ChallengePermission.Unlisted) { LlmModel = "saved-model" });
        await sessions.AcceptAsync(id, new Player(Guid.NewGuid()) { NickName = "LLM — saved-model" });
        var room = GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!;
        await sessions.ExecuteAsync(id, player.Id, new GameCommand(Guid.NewGuid(), "move", 0, "e4"));
        var oldRevision = room.Revision;
        await sessions.ComputerFailedAsync(id, oldRevision);
        Assert.AreEqual("computer-failed", ((AcceptedChallenge)RoomSnapshot.Capture(room).Restore()).Game.ComputerError);
        Assert.IsTrue((await sessions.StateAsync(id, player.Id))!.CanUndo);
        using var search = new CancellationTokenSource(); room.Game.SearchCancellation = search;
        await sessions.ExecuteAsync(id, player.Id, new GameCommand(Guid.NewGuid(), "undo"));
        Assert.IsTrue(search.IsCancellationRequested); Assert.IsNull(room.Game.ComputerError);
        var late = await sessions.ExecuteAsync(id, room.Accepter.Id, new GameCommand(Guid.NewGuid(), "move", 1, "e5", oldRevision), computer: true);
        Assert.AreEqual("stale", late.Error); Assert.AreEqual(0, room.Game.Board.MovesToSan.Count);
        await sessions.ComputerFailedAsync(id, oldRevision);
        Assert.IsNull(room.Game.ComputerError);
    }

    [TestMethod]
    public async Task ResigningCancelsThinkingAndLateFailureCannotChangeTheResult()
    {
        var sessions = GetService<GameSessionService>(); var player = new Player(Guid.NewGuid()) { IsGuest = true };
        var id = await sessions.CreateAsync(new Challenge(player, "LLM resignation", RoleRule.CreatorWhite, TimeSpan.MaxValue, ChallengePermission.Unlisted) { LlmModel = "saved-model" });
        await sessions.AcceptAsync(id, new Player(Guid.NewGuid()));
        var room = GetService<InMemoryDatabase>().GetAcceptedChallenge(id)!;
        using var search = new CancellationTokenSource(); room.Game.SearchCancellation = search;
        var revision = room.Revision;
        await sessions.ExecuteAsync(id, player.Id, new GameCommand(Guid.NewGuid(), "resign"));
        await sessions.ComputerFailedAsync(id, revision);
        Assert.IsTrue(search.IsCancellationRequested); Assert.AreEqual("0-1", room.Result); Assert.IsNull(room.Game.ComputerError);
    }
}
