namespace Aiursoft.ChessServer.Models;

public static class MatingMaterial
{
    // Possibility of mate, not ability to force mate: two knights and helpful opposing material matter.
    public static bool CanMate(string fen, char side)
    {
        var pieces = new List<(char Type, bool Own, int SquareColor)>();
        var rank = 0;
        foreach (var row in fen.Split(' ')[0].Split('/'))
        {
            var file = 0;
            foreach (var symbol in row)
            {
                if (char.IsDigit(symbol)) { file += symbol - '0'; continue; }
                pieces.Add((char.ToLowerInvariant(symbol), char.IsUpper(symbol) == (side == 'w'), (rank + file) % 2));
                file++;
            }
            rank++;
        }
        var own = pieces.Where(p => p.Own && p.Type != 'k').ToArray();
        var other = pieces.Where(p => !p.Own && p.Type != 'k').ToArray();
        if (own.Any(p => p.Type is 'p' or 'r' or 'q')) return true;
        var knights = own.Count(p => p.Type == 'n');
        var bishops = own.Where(p => p.Type == 'b').ToArray();
        if (knights >= 2 || knights > 0 && bishops.Length > 0) return true;
        if (knights == 1) return other.Length > 0;
        if (bishops.Length == 0) return false;
        if (bishops.Select(p => p.SquareColor).Distinct().Count() > 1) return true;
        return other.Any(p => p.Type != 'b' || p.SquareColor != bishops[0].SquareColor);
    }
}
