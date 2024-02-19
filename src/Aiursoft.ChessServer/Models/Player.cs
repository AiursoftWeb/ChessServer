namespace Aiursoft.ChessServer.Models;

public class Player(Guid id)
{
    public Guid Id { get; } = id;

    public string NickName { get; set; } = "Anonymous";
}