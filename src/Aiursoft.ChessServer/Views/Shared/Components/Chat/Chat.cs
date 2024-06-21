
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Views.Shared.Components.Chat;

public class Chat : ViewComponent
{
    public IViewComponentResult Invoke(int gameId)
    {
        return View(new ChatModel
        {
            GameId = gameId
        });
    }
}

public class ChatModel 
{
    public int GameId { get; init; }
}