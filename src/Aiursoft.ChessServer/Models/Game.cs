using Chess;

namespace Aiursoft.ChessServer.Models;

public class Game
{
    public ChessBoard Board { get; set; } = new ChessBoard();

    public Channel Channel { get; set; } = new Channel();
}