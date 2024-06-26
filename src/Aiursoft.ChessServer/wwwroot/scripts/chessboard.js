import { Chess } from "../node_modules/chess.js/dist/esm/chess.js";

const initGameBoard = function (color, player, gameId) {
  let lastMovePair = [null, null];

  fetch(`/games/${gameId}.fen`)
    .then((response) => response.text())
    .then((fen) => {
      let board = null;
      let game = null;
      let whiteSquareGrey = "#a9a9a9";
      let blackSquareGrey = "#696969";
      const wsScheme =
        window.location.protocol === "https:" ? "wss://" : "ws://";
      const socket = new WebSocket(
        `${wsScheme}${window.location.host}/games/${gameId}.ws?playerId=${player}`
      );

      function removeGreySquares() {
        $("#board .square-55d63").css("background", "");
      }

      function greySquare(square) {
        var $square = $("#board .square-" + square);

        var background = whiteSquareGrey;
        if ($square.hasClass("black-3c85d")) {
          background = blackSquareGrey;
        }

        $square.css("background", background);
      }

      function onDragStart(square, piece, position, _) {
        if (game.turn() !== color) {
          return false;
        }

        if (game.isGameOver()) return false;

        if (
          (game.turn() === "w" && piece.search(/^b/) !== -1) ||
          (game.turn() === "b" && piece.search(/^w/) !== -1)
        ) {
          return false;
        }

        const moves = game.moves({
          square: square,
          verbose: true,
        });
        if (moves.length !== 0) {
          greySquare(square);
          for (let i = 0; i < moves.length; i++) {
            greySquare(moves[i].to);
          }
        }
      }

      function onDrop(source, target) {
        removeGreySquares();
        try {
          const move = game.move({
            from: source,
            to: target,
            promotion: "q",
          });
          if (move === null) {
            return "snapback";
          }

          if (source !== target) {
            lastMovePair = [source, target];
          }

          const lastMove = game.history({ verbose: true }).pop().san;
          socket.send(lastMove);
        } catch (e) {
          return "snapback";
        } finally {
          renderTrack();
        }
      }

      function onSnapEnd() {
        board.position(game.fen());
      }

      function onMouseoutSquare(square, piece) {
        // removeGreySquares();
      }

      function renderTrack() {
        if (lastMovePair[0] !== null && lastMovePair[1] !== null) {
          $(
            `#board [data-square!=${lastMovePair[0]}],[data-square!=${lastMovePair[1]}]`
          ).css("box-shadow", "");
          $(
            `#board [data-square=${lastMovePair[0]}],[data-square=${lastMovePair[1]}]`
          ).css("box-shadow", "inset .2px .2px 4px 4px #f9ff49");
        }
      }

      function updateStatusText() {
        let status;
        let moveColor = "White";
        if (game.turn() === "b") {
          moveColor = "Black";
        }
        if (game.isCheckmate()) {
          status = `Game over, ${moveColor} is in checkmate, and winner is ${
            game.turn() === "w" ? "Black" : "White"
          }`;
        } else if (game.isDraw()) {
          status = "Game over, drawn position";
        } else {
          status = `${moveColor} to move`;
          if (game.isCheck()) {
            status += `, ${moveColor} is in check`;
          }
        }
        statusControl.innerHTML = status;
      }

      const config = {
        orientation:
          color === "w"
            ? "white"
            : color === "b"
            ? "black"
            : /*spectator*/ "white",
        draggable: true,
        dragoffBoard: "snapback",
        position: fen,
        onDragStart: onDragStart,
        onSnapEnd: onSnapEnd,
        onDrop: onDrop,
        onMouseoutSquare: onMouseoutSquare,
      };
      board = ChessBoard("board", config);
      const statusControl = document.getElementById("status");
      const roleControl = document.getElementById("role");
      roleControl.innerHTML = `You are ${
        color === "w" ? "White" : color === "b" ? "Black" : "Spectator"
      } player.`;

      function refresh(newFen) {
        renderTrack();

        game = new Chess(newFen);
        board.position(newFen);
        console.log(`Got fen ${newFen}. refreshing board...`);
        updateStatusText();
      }

      refresh(fen);

      socket.onmessage = function (event) {
        if (game !== null) {
          let m = findMove(game.fen(), event.data);
          let p1 = m.p1;
          let p2 = m.p2;
          if (p1 !== null && p2 !== null) {
            lastMovePair = [p1, p2];
          }
        }
        refresh(event.data);
      };

      socket.onclose = function () {
        setTimeout(function () {
          initGameBoard(color, player, gameId);
        }, 1000);
      };
    });
};

function parseFEN(fen) {
  const parts = fen.split(" ")[0].split("/");
  const board = [];

  for (const part of parts) {
    const row = [];
    for (const char of part) {
      if (isNaN(char)) {
        row.push(char);
      } else {
        for (let i = 0; i < parseInt(char); i++) {
          row.push("");
        }
      }
    }
    board.push(row);
  }

  return board;
}

function findMove(fen1, fen2) {
  const board1 = parseFEN(fen1);
  const board2 = parseFEN(fen2);

  let p1 = null;
  let p2 = null;

  for (let i = 0; i < 8; i++) {
    for (let j = 0; j < 8; j++) {
      if (board1[i][j] !== board2[i][j]) {
        if (p1 === null) {
          p1 = { row: i, col: j };
        } else {
          p2 = { row: i, col: j };
        }
      }
    }
  }

  if (p1 === null || p2 === null) {
    return { p1, p2 };
  } else {
    let r = 8 - p1.row;
    let c = String.fromCharCode("a".charCodeAt() + p1.col);
    p1 = c + r;

    r = 8 - p2.row;
    c = String.fromCharCode("a".charCodeAt() + p2.col);
    p2 = c + r;
    return { p1, p2 };
  }
}

export default initGameBoard;
