using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.ChessServer;

[ExcludeFromCodeCoverage]
public abstract class Program
{
    public static async Task Main(string[] args)
    {
        var app = await WebTools.Extends.AppAsync<Startup>(args);
        await app.RunAsync();
    }
}