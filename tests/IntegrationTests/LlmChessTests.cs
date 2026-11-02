using Aiursoft.ChessServer.Configuration;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services;
using Aiursoft.GptClient.Abstractions;
using Aiursoft.GptClient.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.ChessServer.Tests.IntegrationTests;

[TestClass]
public class LlmChessTests
{
    private const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static OpenAIConfiguration Configuration => new() { CompletionApiUrl = "https://gateway.example/v1/chat/completions", Model = "chess-model" };

    private sealed class Answers(HttpClient http, Func<OpenAiRequestModel, CancellationToken, Task<CompletionData>> answer)
        : ChatClient(http, NullLogger<ChatClient>.Instance)
    {
        public List<OpenAiRequestModel> Requests { get; } = [];
        public override Task<CompletionData> AskModel(OpenAiRequestModel model, string completionApiUrl, string? token, CancellationToken cancellationToken)
        {
            Requests.Add(model);
            return answer(model, cancellationToken);
        }
    }

    private static Task<CompletionData> Text(string value)
    {
        var response = new CompletionData(); response.SetContent(value); return Task.FromResult(response);
    }

    [TestMethod]
    [DataRow("e4")]
    [DataRow("<think>Compare e4 and d4.</think>\ne4")]
    public async Task InstructAndThinkingAnswersSelectOnlyOneLegalMove(string answer)
    {
        using var http = new HttpClient();
        var client = new Answers(http, (_, _) => Text(answer));
        var move = await new LlmChessPlayer(client, Options.Create(Configuration)).GetMoveAsync(InitialFen, "[Result \"*\"]\n*", "saved-model", CancellationToken.None);
        Assert.AreEqual("e4", move);
        var request = client.Requests.Single();
        Assert.AreEqual("saved-model", request.Model);
        Assert.IsNull(request.Temperature); Assert.IsNull(request.PresencePenalty);
        Assert.AreEqual(2, request.Messages.Count);
        Assert.AreEqual("system", request.Messages[0].Role);
        StringAssert.Contains(request.Messages[0].Content, "Return exactly one entry");
        Assert.AreEqual("user", request.Messages[1].Role);
        StringAssert.Contains(request.Messages[1].Content, InitialFen);
        StringAssert.Contains(request.Messages[1].Content, "ASCII board (uppercase = White, lowercase = Black, . = empty):");
        StringAssert.Contains(request.Messages[1].Content, "8 │ r  n  b  q  k  b  n  r │");
        StringAssert.Contains(request.Messages[1].Content, "1 │ R  N  B  Q  K  B  N  R │");
        StringAssert.Contains(request.Messages[1].Content, "[Result \"*\"]");
        StringAssert.Contains(request.Messages[1].Content, "Legal SAN moves:");
    }

    [TestMethod]
    public async Task IllegalAnswerGetsOneStatelessCorrection()
    {
        using var http = new HttpClient(); var attempt = 0;
        var client = new Answers(http, (_, _) => Text(attempt++ == 0 ? "Ke9" : "d4"));
        Assert.AreEqual("d4", await new LlmChessPlayer(client, Options.Create(Configuration)).GetMoveAsync(InitialFen, "*", "chess-model", CancellationToken.None));
        Assert.AreEqual(2, client.Requests.Count);
        Assert.IsTrue(client.Requests.All(r => r.Messages.Count == 2));
        Assert.AreEqual(client.Requests[0].Messages[0].Content, client.Requests[1].Messages[0].Content);
        Assert.AreEqual("system", client.Requests[1].Messages[0].Role);
        Assert.AreEqual("user", client.Requests[1].Messages[1].Role);
        StringAssert.Contains(client.Requests[1].Messages[1].Content, "previous final answer");
        StringAssert.Contains(client.Requests[1].Messages[1].Content, "8 │ r  n  b  q  k  b  n  r │");
    }

    [TestMethod]
    public async Task AsciiBoardShowsTheCurrentPositionForBlack()
    {
        const string fen = "4k3/8/8/3pP3/8/8/8/4K3 b - - 0 2";
        using var http = new HttpClient();
        var client = new Answers(http, (_, _) => Text("d4"));
        Assert.AreEqual("d4", await new LlmChessPlayer(client, Options.Create(Configuration)).GetMoveAsync(fen, "*", "chess-model", CancellationToken.None));
        var prompt = client.Requests.Single().Messages[1].Content;
        StringAssert.Contains(prompt, "You are playing Black.");
        StringAssert.Contains(prompt, "8 │ .  .  .  .  k  .  .  . │");
        StringAssert.Contains(prompt, "5 │ .  .  .  p  P  .  .  . │");
        StringAssert.Contains(prompt, "1 │ .  .  .  .  K  .  .  . │");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("<think>e4")]
    [DataRow("e4 e5")]
    [DataRow("1. e4")]
    [DataRow("The best move is e4.")]
    [DataRow("```\ne4\n```")]
    public async Task MissingTruncatedOrMultipleMovesNeverBecomeAnEngineMove(string answer)
    {
        using var http = new HttpClient();
        var client = new Answers(http, (_, _) => Text(answer));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new LlmChessPlayer(client, Options.Create(Configuration)).GetMoveAsync(InitialFen, "*", "chess-model", CancellationToken.None));
        Assert.AreEqual(2, client.Requests.Count);
    }

    [TestMethod]
    [DataRow("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "0-0", "O-O")]
    [DataRow("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", "a8=Q+", "a8=Q+")]
    [DataRow("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2", "exd6", "exd6")]
    [DataRow("4k3/8/8/8/8/8/8/1N2KN2 w - - 0 1", "Nbd2", "Nbd2")]
    [DataRow("7k/5Q2/6K1/8/8/8/8/8 w - - 0 1", "Qg7#", "Qg7#")]
    public async Task ServerNotationHandlesSpecialMoves(string fen, string answer, string expected)
    {
        using var http = new HttpClient(); var client = new Answers(http, (_, _) => Text(answer));
        Assert.AreEqual(expected, await new LlmChessPlayer(client, Options.Create(Configuration)).GetMoveAsync(fen, "*", "chess-model", CancellationToken.None));
    }

    [TestMethod]
    public async Task SlowThinkingStopsAtTheConfiguredDeadline()
    {
        using var http = new HttpClient();
        var client = new Answers(http, async (_, token) => { await Task.Delay(Timeout.Infinite, token); return await Text("e4"); });
        var settings = new OpenAIConfiguration { CompletionApiUrl = Configuration.CompletionApiUrl, Model = "chess-model", TimeoutSeconds = 1 };
        var progress = new List<ComputerProgress>();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => new LlmChessPlayer(client, Options.Create(settings)).GetMoveAsync(InitialFen, "*", "chess-model", CancellationToken.None,
            update => { progress.Add(update); return Task.CompletedTask; }));
        Assert.AreEqual(3, client.Requests.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, progress.Where(p => !p.Queued).Select(p => p.Attempt).ToArray());
    }

    [TestMethod]
    public async Task TimedOutAttemptCanRetryAndReturnOneLegalMove()
    {
        using var http = new HttpClient();
        var attempts = 0;
        var client = new Answers(http, async (_, token) =>
        {
            if (++attempts == 1) await Task.Delay(Timeout.Infinite, token);
            return await Text("e4");
        });
        var settings = new OpenAIConfiguration { CompletionApiUrl = Configuration.CompletionApiUrl, TimeoutSeconds = 1 };
        var result = await new LlmChessPlayer(client, Options.Create(settings)).GetMoveResultAsync(InitialFen, "*", "chess-model", CancellationToken.None);
        Assert.AreEqual("e4", result.Move); Assert.AreEqual(2, result.Attempts);
    }

    [TestMethod]
    public async Task CancelledGameDoesNotRetryOrCommitALateAnswer()
    {
        using var http = new HttpClient();
        using var cancel = new CancellationTokenSource();
        var answer = new TaskCompletionSource<CompletionData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Answers(http, (_, _) => answer.Task);
        var move = new LlmChessPlayer(client, Options.Create(Configuration)).GetMoveAsync(InitialFen, "*", "chess-model", cancel.Token);
        cancel.Cancel(); answer.SetResult(await Text("e4"));
        await Assert.ThrowsAsync<OperationCanceledException>(() => move);
        Assert.AreEqual(1, client.Requests.Count);
    }

    [TestMethod]
    public async Task GameCancellationDuringRetryStopsImmediately()
    {
        using var http = new HttpClient();
        using var cancel = new CancellationTokenSource();
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var client = new Answers(http, async (_, token) =>
        {
            if (Interlocked.Increment(ref requests) == 2) retryStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return await Text("e4");
        });
        var settings = new OpenAIConfiguration { CompletionApiUrl = Configuration.CompletionApiUrl, TimeoutSeconds = 1 };
        var move = new LlmChessPlayer(client, Options.Create(settings)).GetMoveAsync(InitialFen, "*", "chess-model", cancel.Token);
        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => move);
        Assert.AreEqual(2, client.Requests.Count);
    }
}
