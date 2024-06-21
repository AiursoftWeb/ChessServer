namespace Aiursoft.ChessServer.Models;

public class ChatMessage(string content, Player sender)
{
    public string Content { get; set; } = content;
    public Player Sender { get; set; } = sender;
}