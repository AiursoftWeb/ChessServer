using Aiursoft.CSTools.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Aiursoft.WebTools.Extends;

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
}