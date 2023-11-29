using AiurObserver;

namespace Aiursoft.ChessServer.Models;


public class Channel : AsyncObservable<Message>
{
    public Channel()
    {
        Messages.AddLast(new Message("placeholder"));
    }

    private LinkedList<Message> Messages { get; } = new();

    public async Task Push(Message message)
    {
        Messages.AddLast(message);
        await Task.WhenAll(Broadcast(message));
    }
}