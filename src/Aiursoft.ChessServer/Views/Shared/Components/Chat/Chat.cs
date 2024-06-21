
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Views.Shared.Components.Chat;

public class Chat : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        return View();
    }
}