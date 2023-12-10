using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.ChessServer.Views.Shared.Components;

public class ChessBoard : ViewComponent
{
    public IViewComponentResult Invoke(int gameId)
    {
        return View(new ChessBoardModel
        {
            GameId = gameId
        });
    }
}

public class ChessBoardModel 
{
    public int GameId { get; set; }
}