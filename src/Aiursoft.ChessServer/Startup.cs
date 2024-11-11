using System.Reflection;
using Aiursoft.ChessServer.Middlewares;
using Aiursoft.ChessServer.Models;
using Aiursoft.ChessServer.Services;
using Aiursoft.InMemoryKvDb;
using Aiursoft.Scanner;
using Aiursoft.WebTools.Abstractions.Models;

namespace Aiursoft.ChessServer;

public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        services.AddLibraryDependencies();

        services.AddLruMemoryStore<Player, Guid>(
            id => new Player(id) { NickName = "Anonymous " + new Random().Next(1000, 9999) },
            maxCachedItemsCount: 1024);
        
        services.AddLruMemoryStoreManualCreate<Challenge, int>(
            maxCachedItemsCount: 256);

        services.AddTransient<ChessEngine>();
        
        services
            .AddControllersWithViews()
            .AddApplicationPart(Assembly.GetExecutingAssembly());
    }

    public void Configure(WebApplication app)
    {
        app.UseMiddleware<AllowCrossOriginMiddleware>();
        app.UseStaticFiles();
        app.UseRouting();
        app.MapDefaultControllerRoute();
        app.UseWebSockets();
    }
}