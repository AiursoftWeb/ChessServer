using System.Collections.Concurrent;

namespace Aiursoft.ChessServer.Models;

public class Message
{
    public Message(string content)
    {
        Content = content;
    }

    public int Id { get; init; }
    public string Content { get; set; }
}

public class Channel
{
    private ConcurrentBag<Message> Messages { get; } = new();

    public ConcurrentBag<SemaphoreSlim> HasNewMessageBlocker { get; } = new();

    public IEnumerable<Message> GetMessagesFrom(int lastReadId)
    {
        return Messages.Where(t => t.Id > lastReadId);
    }

    public void Push(Message message)
    {
        Messages.Add(message);
        foreach (var blocker in HasNewMessageBlocker)
        {
            blocker.Release();
        }
    }
    
    public bool UnRegister(out SemaphoreSlim blocker)
    {
        return HasNewMessageBlocker.TryTake(out blocker!);
    }
}