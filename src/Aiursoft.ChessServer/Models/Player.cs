namespace Aiursoft.ChessServer.Models;

public class Player
{
    public string NickName { get; set; } = "Anonymous";
    public Guid Id { get; set; } = Guid.NewGuid();
}