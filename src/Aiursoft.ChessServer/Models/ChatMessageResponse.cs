namespace Aiursoft.ChessServer.Models;

public class ChatMessageResponse(ChatMessage message, Guid currentUserId)
{
    public string Content { get; set; } = message.Content;
    public string SenderNickName { get; set; } = message.Sender.NickName;
    public bool IsMe { get; set; } = message.Sender.Id == currentUserId;
}