namespace Aiursoft.ChessServer.Models;

public class ChallengeViewModel
{
    public int RoomId { get; set; }
    
    public Guid PlayerId { get; set; }
    
    public bool IsCreator { get; set; }
}