using System.Reflection;
using Aiursoft.ChessServer.Middlewares;
using Aiursoft.Scanner;
using Aiursoft.WebTools.Models;

namespace Aiursoft.ChessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var app = WebTools.Extends.App<Startup>(args);
        await app.RunAsync();
    }
}

public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        services.AddLibraryDependencies();
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
    }
}
