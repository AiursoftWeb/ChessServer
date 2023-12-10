using System.Net.WebSockets;
using Aiursoft.CSTools.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        _server = App<Startup>(Array.Empty<string>(), port: _port);
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    [TestMethod]
    [DataRow("/")]
    public async Task GetHome(string url)
    {
        var response = await _http.GetAsync(_endpointUrl + url);
        response.EnsureSuccessStatusCode(); // Status Code 200-299
    }

    [TestMethod]
    [DataRow("/games/12345.json")]
    [DataRow("/games/12345.ascii")]
    [DataRow("/games/12345.fen")]
    [DataRow("/games/12345.pgn")]
    public async Task GetChess(string url)
    {
        var response = await _http.GetAsync(_endpointUrl + url);
        response.EnsureSuccessStatusCode(); // Status Code 200-299
    }

    [TestMethod]
    [DataRow("/games/1/move/w/e4")]
    [DataRow("/games/2/move/w/d4")]
    [DataRow("/games/3/move/w/Nf3")]
    [DataRow("/games/4/move/w/Nc3")]
    public async Task MoveChess(string url)
    {
        var response = await _http.PostAsync(_endpointUrl + url, new StringContent(""));
        response.EnsureSuccessStatusCode(); // Status Code 200-299
    }

    [TestMethod]
    [DataRow("/games/5/move/w/O-O")]
    [DataRow("/games/6/move/b/O-O-O")]
    public async Task InvalidMoveChess(string url)
    {
        var response = await _http.PostAsync(_endpointUrl + url, new StringContent(""));
        Assert.AreEqual(400, (int)response.StatusCode);
    }

    [TestMethod]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    public async Task TestConnect(int gameId)
    {
        var tester = new WebSocketTester();
        var socket = new ClientWebSocket();
        
        await socket.ConnectAsync(new Uri(_endpointUrl.Replace("http", "ws") + $"/games/{gameId}.ws"),
            CancellationToken.None);
        await Task.Factory.StartNew(() => tester.Monitor(socket));
        
        await _http.PostAsync(_endpointUrl + $"/games/{gameId}/move/w/e4", new StringContent(""));
        await Task.Delay(50);
        Assert.AreEqual("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", tester.LastMessage);
        
        await _http.PostAsync(_endpointUrl + $"/games/{gameId}/move/b/e5", new StringContent(""));
        await Task.Delay(50);
        Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", tester.LastMessage);
        
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(10)]
    public async Task TestGameWithReconnection(int gameId)
    {
        var socket1 = new ClientWebSocket();
        var tester1 = new WebSocketTester();
        await socket1.ConnectAsync(new Uri(_endpointUrl.Replace("http", "ws") + $"/games/{gameId}.ws"),
            CancellationToken.None);
        await Task.Factory.StartNew(() => tester1.Monitor(socket1));

        var socket2 = new ClientWebSocket();
        var tester2 = new WebSocketTester();
        await socket2.ConnectAsync(new Uri(_endpointUrl.Replace("http", "ws") + $"/games/{gameId}.ws"),
            CancellationToken.None);
        await Task.Factory.StartNew(() => tester2.Monitor(socket2));
        
        await _http.PostAsync(_endpointUrl + $"/games/{gameId}/move/w/e4", new StringContent(""));
        await Task.Delay(50);
        Assert.AreEqual("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", tester1.LastMessage);
        Assert.AreEqual("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", tester2.LastMessage);
        
        await _http.PostAsync(_endpointUrl + $"/games/{gameId}/move/b/e5", new StringContent(""));
        await Task.Delay(50);
        Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", tester1.LastMessage);
        Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", tester2.LastMessage);
        
        await socket1.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        var socket3 = new ClientWebSocket();
        var tester3 = new WebSocketTester();
        await socket3.ConnectAsync(new Uri(_endpointUrl.Replace("http", "ws") + $"/games/{gameId}.ws"),
            CancellationToken.None);
        await Task.Factory.StartNew(() => tester3.Monitor(socket3));
        
        await _http.PostAsync(_endpointUrl + $"/games/{gameId}/move/w/Nf3", new StringContent(""));
        await Task.Delay(50);
        Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2", tester1.LastMessage);
        Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", tester2.LastMessage);
        Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", tester3.LastMessage);
    }
}