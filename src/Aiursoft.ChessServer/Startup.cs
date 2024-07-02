using System.Reflection;
using Aiursoft.ChessServer.Middlewares;
using Aiursoft.ChessServer.Services;
using Aiursoft.Scanner;
using Aiursoft.WebTools.Abstractions.Models;

namespace Aiursoft.ChessServer;

public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        services.AddLibraryDependencies();

        services.AddSingleton<ChessEngine>();
        
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