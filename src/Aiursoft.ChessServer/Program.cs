namespace Aiursoft.ChessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var app = WebTools.Extends.App<Startup>(args);
        await app.RunAsync();
    }
}