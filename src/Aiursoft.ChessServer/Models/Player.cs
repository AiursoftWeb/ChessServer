namespace Aiursoft.ChessServer.Models;

public class Player(Guid id)
{
    public Guid Id { get; } = id;

    public bool IsGuest { get; set; }

    public string NickName { get; set; } = "Player";
}