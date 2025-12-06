using System.Diagnostics;
using Aiursoft.AiurObserver;
using Aiursoft.AiurObserver.DefaultConsumers;
using Aiursoft.AiurObserver.WebSocket;
using Aiursoft.ChessServer.Data;
using Aiursoft.ChessServer.Models;
using Aiursoft.CSTools.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using static Aiursoft.WebTools.Extends;
// ReSharper disable StringLiteralTypo

namespace Aiursoft.ChessServer.Tests;

[TestClass]
public class BasicTests
{
    private readonly string _endpointUrl;
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public BasicTests()
    {
        _port = Network.GetAvailablePort();
        _endpointUrl = $"http://localhost:{_port}";
        _http = new HttpClient();
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        _server = await AppAsync<Startup>([], port: _port);
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    private void CreateChallengeForTest(int id)
    {
        var db = _server!.Services.GetRequiredService<InMemoryDatabase>();
        if (db.GetChallenge(id) != null)
        {
            return;
        }

        db.CreateChallenge(12345, new AcceptedChallenge(
            creator: new Player(Guid.NewGuid()),
            accepter: new Player(Guid.NewGuid()),
            message: "Test",
            roleRule: RoleRule.Random,
            timeLimit: TimeSpan.FromMinutes(10),
            permission: ChallengePermission.Public,
            challengeChangedChannel: new AsyncObservable<string>()));
    }

    [TestMethod]
    [DataRow("/")]
    public async Task GetHome(string url)
    {
        var response = await _http.GetAsync(_endpointUrl + url);
        response.EnsureSuccessStatusCode(); // Status Code 200-299
    }

    [TestMethod]
    [DataRow("/games/12345.html")]
    [DataRow("/games/12345.json")]
    [DataRow("/games/12345.ascii")]
    [DataRow("/games/12345.fen")]
    [DataRow("/games/12345.pgn")]
    public async Task GetChess(string url)
    {
        CreateChallengeForTest(12345);

        var response = await _http.GetAsync(_endpointUrl + url);
        response.EnsureSuccessStatusCode(); // Status Code 200-299
    }

    [TestMethod]
    [DataRow("e4")]
    [DataRow("d4")]
    [DataRow("Nf3")]
    [DataRow("Nc3")]
    public async Task MoveChess(string action)
    {
        CreateChallengeForTest(12345);

        var endPoint = _endpointUrl.Replace("http", "ws") + $"/games/12345.ws?playerId={Guid.NewGuid()}";
        var socket = await endPoint.ConnectAsWebSocketServer();
        var socketStage = new MessageStageLast<string>();
        socket.Subscribe(socketStage);
        await Task.Factory.StartNew(() => socket.Listen());

        await socket.Send(action);

        var waitMaxTime = new Stopwatch();
        waitMaxTime.Start();
        while (string.IsNullOrWhiteSpace(socketStage.Stage))
        {
            await Task.Delay(150);
            if (!string.IsNullOrWhiteSpace(socketStage.Stage))
            {
                break;
            }

            if (waitMaxTime.ElapsedMilliseconds > 5000)
            {
                Assert.Fail("Timeout that the server did not respond.");
            }
        }

        await socket.Close();
    }

    [TestMethod]
    [DataRow("e4")]
    [DataRow("d4")]
    [DataRow("Nf3")]
    [DataRow("Nc3")]
    public async Task InvalidMoveChess(string action)
    {
        CreateChallengeForTest(12345);

        var endPoint = _endpointUrl.Replace("http", "ws") + $"/games/12345.ws?playerId={Guid.NewGuid()}";
        var socket = await endPoint.ConnectAsWebSocketServer();
        var socketStage = new MessageStageLast<string>();
        socket.Subscribe(socketStage);
        await Task.Factory.StartNew(() => socket.Listen());

        await socket.Send(action);
        await Task.Delay(150);

        // fen equal init
        Assert.AreEqual("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", socketStage.Stage);
        await socket.Close();
    }

    // TODO Refactor tests!

    // [TestMethod]
    // [DataRow(7)]
    // [DataRow(8)]
    // [DataRow(9)]
    // public async Task TestConnect(int gameId)
    // {
    //     var endPoint = _endpointUrl.Replace("http", "ws") + $"/games/{gameId}.ws?playerId={Guid.NewGuid()}";
    //     var socket = await endPoint.ConnectAsWebSocketServer();
    //     var socketStage = new MessageStageLast<string>();
    //     socket.Subscribe(socketStage);
    //     await Task.Factory.StartNew(() => socket.Listen());
    //
    //     await socket.Send("e4");
    //     await Task.Delay(150);
    //     Assert.AreEqual("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", socketStage.Stage);
    //
    //     await socket.Close();
    // }
    //
    // [TestMethod]
    // [DataRow(10)]
    // [DataRow(11)]
    // public async Task TestGameWithReconnection(int gameId)
    // {
    //     var socket1 = await (_endpointUrl.Replace("http", "ws") + $"/games/{gameId}.ws?player=w")
    //         .ConnectAsWebSocketServer();
    //     var socket1Stage = new MessageStageLast<string>();
    //      socket1.Subscribe(socket1Stage);
    //     await Task.Factory.StartNew(() => socket1.Listen());
    //
    //     var socket2 = await (_endpointUrl.Replace("http", "ws") + $"/games/{gameId}.ws?player=b")
    //         .ConnectAsWebSocketServer();
    //     var socket2Stage = new MessageStageLast<string>();
    //     socket2.Subscribe(socket2Stage);
    //     await Task.Factory.StartNew(() => socket2.Listen());
    //
    //     await socket1.Send("e4");
    //     await Task.Delay(150);
    //     Assert.AreEqual("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", socket1Stage.Stage);
    //     Assert.AreEqual("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", socket2Stage.Stage);
    //
    //     await socket2.Send("e5");
    //     await Task.Delay(150);
    //     Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", socket1Stage.Stage);
    //     Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", socket2Stage.Stage);
    //
    //     await socket1.Close();
    //
    //     var socket3 = await (_endpointUrl.Replace("http", "ws") + $"/games/{gameId}.ws?player=w")
    //         .ConnectAsWebSocketServer();
    //     var socket3Stage = new MessageStageLast<string>();
    //     socket3.Subscribe(socket3Stage);
    //     await Task.Factory.StartNew(() => socket3.Listen());
    //
    //     await socket3.Send("Nf3");
    //     await Task.Delay(150);
    //     Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", socket1Stage.Stage);
    //     Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", socket2Stage.Stage);
    //     Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", socket3Stage.Stage);
    //
    //     await socket3.Close();
    //     await socket2.Close();
    // }
}
