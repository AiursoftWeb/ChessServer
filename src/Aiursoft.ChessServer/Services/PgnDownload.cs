using System.Globalization;
using System.Text;

namespace Aiursoft.ChessServer.Services;

internal static class PgnDownload
{
    public const string ContentType = "application/x-chess-pgn";

    public static byte[] Encode(string pgn)
    {
        // Some desktop readers parse tag values byte by byte. Keep their input
        // printable ASCII while retaining a code point for names outside ASCII.
        var tagsEnd = 0;
        while (tagsEnd < pgn.Length && pgn[tagsEnd] == '[')
        {
            var newline = pgn.IndexOf('\n', tagsEnd);
            tagsEnd = newline < 0 ? pgn.Length : newline + 1;
        }

        var export = new StringBuilder(pgn.Length);
        foreach (var rune in pgn.AsSpan(0, tagsEnd).EnumerateRunes())
        {
            var value = rune.Value;
            if (value is >= ' ' and <= '~' or '\r' or '\n' or '\t')
            {
                export.Append((char)value);
            }
            else
            {
                switch (Rune.GetUnicodeCategory(rune))
                {
                    case UnicodeCategory.DashPunctuation:
                        export.Append('-');
                        break;
                    case UnicodeCategory.SpaceSeparator:
                        export.Append(' ');
                        break;
                    default:
                        export.Append(CultureInfo.InvariantCulture, $"[U+{value:X4}]");
                        break;
                }
            }
        }
        export.Append(pgn.AsSpan(tagsEnd));
        return Encoding.UTF8.GetBytes(export.ToString());
    }
}
