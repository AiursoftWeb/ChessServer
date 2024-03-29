﻿namespace Aiursoft.ChessServer.Middlewares
{
    public class AllowCrossOriginMiddleware(RequestDelegate next)
    {
        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path.ToString().StartsWith("/games/img")) 
            {
                context.Request.Path = context.Request.Path.ToString()["/games".Length..];
            }

            var origin = context.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin))
            {
                await next.Invoke(context);
                return;
            }

            context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Append("Access-Control-Allow-Credentials", "true");
            context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Accept, Authorization");

            if (context.Request.Method == "OPTIONS" && !context.Response.HasStarted)
            {
                context.Response.StatusCode = 200;
                return;
            }

            await next.Invoke(context);
        }
    }
}