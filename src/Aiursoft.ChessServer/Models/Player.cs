namespace Aiursoft.ChessServer.Models;

public class Player
{
    public Guid Id { get; }
    
    public Player(Guid id)
    {
        Id = id;
    }
    
    public string NickName { get; set; } = "Anonymous";
    
}