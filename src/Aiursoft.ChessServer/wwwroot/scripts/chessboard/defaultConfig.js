const WHITE_ABBREVIATION = "w";
const BLACK_ABBREVIATION = "b";

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
 * let onDrop = onDrop({game, socket});
 * ```
 *
 * @param {{game, socket}} globalParams
 * @returns onDrop function
 */
function buildOnDrop(globalParams) {
  const realOnDrop = (source, target) => {
    let game = globalParams.game;
    let socket = globalParams.socket;

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

      // if (source !== target) {
      //   lastMovePair = [source, target];
      // }

      const lastMove = game.history({ verbose: true }).pop().san;
      socket.send(lastMove);
    } catch (e) {
      return "snapback";
    } finally {
      // renderTrack();
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

export {
  buildOnDragStart,
  buildOnDrop,
  buildOnSnapEnd,
  WHITE_ABBREVIATION,
  BLACK_ABBREVIATION,
  WHITE_SQUARE_GREY,
  BLACK_SQUARE_GREY,
};
