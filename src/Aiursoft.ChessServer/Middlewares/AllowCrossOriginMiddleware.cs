namespace Aiursoft.ChessServer.Middlewares
{
    public class AllowCrossOriginMiddleware
    {
        private readonly RequestDelegate _next;

        public AllowCrossOriginMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path.ToString().StartsWith("/games/img")) 
            {
                context.Request.Path = context.Request.Path.ToString().Substring("/games".Length);
            }

            var origin = context.Request.Headers["Origin"].ToString();
            if (string.IsNullOrEmpty(origin))
            {
                await _next.Invoke(context);
                return;
            }

            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept, Authorization");

            if (context.Request.Method == "OPTIONS" && !context.Response.HasStarted)
            {
                context.Response.StatusCode = 200;
                return;
            }

            await _next.Invoke(context);
        }
    }
}