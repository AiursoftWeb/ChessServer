using Aiursoft.Scanner.Abstractions;
using System.Collections.Concurrent;
using Aiursoft.ChessServer.Models;
using Chess;

namespace Aiursoft.ChessServer.Data;

public class InMemoryDatabase : ISingletonDependency
{
    private ConcurrentDictionary<int, Game> Games { get; } = new();

    public GameContext[] GetActiveGames()
    {
        return Games.Select(g => new GameContext(g.Value, g.Key)).ToArray();
    }

    public Game GetOrAddGame(int id)
    {
        return Games.GetOrAdd(id, _ => new Game());
    }
}

public class GameContext
{
    public GameContext(Game game, int id)
    {
        Turn = game.Board.Turn.AsChar;
        Ended = game.Board.IsEndGame;
        End = game.Board.EndGame?.EndgameType;
        Won = game.Board.EndGame?.WonSide;
        MoveIndex = game.Board.MoveIndex;
        WhiteKingChecked = game.Board.WhiteKingChecked;
        BlackKingChecked = game.Board.BlackKingChecked;
        links = new Dictionary<string, string>
            {
                { "ascii", $"games/{id}.ascii" },
                { "fen", $"games/{id}.fen" },
                { "pgn", $"games/{id}.pgn" },
                { "html", $"games/{id}.html" },
                { "websocket", $"games/{id}.ws" },
                { "move-post", $"games/{id}/move/{{player}}/{{move_algebraic_notation}}" }
            };
        Listeners = game.Channel.GetListenerCount();
    }
    public Dictionary<string, string> links { get; internal set; }

    public char Turn { get; internal set; }
    public bool Ended { get; internal set; }
    public EndgameType? End { get; internal set; }
    public PieceColor? Won { get; internal set; }
    public int MoveIndex { get; internal set; }
    public bool WhiteKingChecked { get; internal set; }
    public bool BlackKingChecked { get; internal set; }
    public int Listeners { get; internal set; }
}