const WHITE_ABBREVIATION = "w";
const BLACK_ABBREVIATION = "b";
const WHITE = "White";
const BLACK = "Black";

const WHITE_SQUARE_GREY = "#a9a9a9";
const BLACK_SQUARE_GREY = "#696969";

/**
 * return the onDrapStart function
 *
 * # example:
 * ```js
 * let onDrapStart = onDrapStart({game, color});
 * ```
 *
 * @param {{game, color}} globalParams
 * @returns onDrapStart function
 */
function buildOnDragStart(globalParams) {
  const realOnDrapStart = (square, piece, position, _) => {
    let game = globalParams.game;
    let color = globalParams.color;

    if (game.turn() !== color) {
      return false;
    }

    if (game.isGameOver()) return false;

    if (
      (game.turn() === WHITE_ABBREVIATION &&
        piece.startsWith(BLACK_ABBREVIATION)) ||
      (game.turn() === BLACK_ABBREVIATION &&
        piece.startsWith(WHITE_ABBREVIATION))
    ) {
      return false;
    }

    const moves = game.moves({
      square: square,
      verbose: true,
    });
    if (moves.length !== 0) {
      greySquare(square);
      moves.forEach((square) => {
        greySquare(square.to);
      });
    }
  };

  return realOnDrapStart;

  function greySquare(square) {
    const squareEl = document.querySelector(`#board [data-square=${square}]`);

    const background = squareEl.classList.contains("black-3c85d")
      ? BLACK_SQUARE_GREY
      : WHITE_SQUARE_GREY;

    squareEl.style.backgroundColor = background;
  }
}

/**
 * return the onDrop function
 *
 * # example:
 * ```js
 * let onDrop = onDrop({game, socket, lastMovePair, render});
 * ```
 *
 * @param {{game, socket}} globalParams
 * @returns onDrop function
 */
function buildOnDrop(globalParams) {
  const realOnDrop = (source, target) => {
    let game = globalParams.game;
    let socket = globalParams.socket;
    let render = globalParams.render;

    document.querySelectorAll("#board [data-square]").forEach((square) => {
      square.style.backgroundColor = "";
    });

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
        globalParams.lastMovePair = [source, target];
        render();
      }

      const lastMove = game.history({ verbose: true }).pop().san;
      socket.send(lastMove);
    } catch (e) {
      return "snapback";
    }
  };

  return realOnDrop;
}

/**
 * return the onSnapEnd function
 *
 * # example:
 * ```js
 * let onSnapEnd = onSnapEnd({game, board});
 * ```
 *
 * @param {{game, board}} globalParams
 * @returns onSnapEnd function
 */
function buildOnSnapEnd(globalParams) {
  const realOnSnapEnd = () => {
    let board = globalParams.board;
    let game = globalParams.game;

    board.position(game.fen());
  };

  return realOnSnapEnd;
}

function buildOnChange(globalParams) {
  const onChange = (oldPos, newPos) => {
    globalParams._playSound();
  };

  return onChange;
}

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

/**
 * find the move positions
 * @param {oldFEN} fen1 old FEN
 * @param {string} fen2 new FEN
 * @returns two positions thay are move, e.g. ['e4', 'e6']
 */
function findMove(fen1, fen2) {
  const board1 = parseFEN(fen1);
  const board2 = parseFEN(fen2);

  let position1 = null;
  let position2 = null;

  for (let i = 0; i < 8; i++) {
    for (let j = 0; j < 8; j++) {
      if (board1[i][j] !== board2[i][j]) {
        if (position1 === null) {
          position1 = { row: i, col: j };
        } else {
          position2 = { row: i, col: j };
        }
      }
    }
  }

  if (position1 === null || position2 === null) {
    return [position1, position2];
  } else {
    let row = 8 - position1.row;
    let col = String.fromCharCode("a".charCodeAt() + position1.col);
    position1 = col + row;

    row = 8 - position2.row;
    col = String.fromCharCode("a".charCodeAt() + position2.col);
    position2 = col + row;
    return [position1, position2];
  }
}

export {
  buildOnDragStart,
  buildOnDrop,
  buildOnSnapEnd,
  buildOnChange,
  WHITE_ABBREVIATION,
  BLACK_ABBREVIATION,
  WHITE_SQUARE_GREY,
  BLACK_SQUARE_GREY,
  WHITE,
  BLACK,
  findMove,
};
