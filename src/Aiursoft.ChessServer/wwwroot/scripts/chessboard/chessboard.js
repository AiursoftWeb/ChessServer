import { Chess } from "../../node_modules/chess.js/dist/esm/chess.js";

import {
  buildOnDragStart,
  buildOnDrop,
  buildOnSnapEnd,
  WHITE_ABBREVIATION,
  BLACK_ABBREVIATION,
  WHITE,
  BLACK,
  findMove,
} from "./defaultConfig.js";

/**
 * use this builder to build a chess with default configuration
 *
 * # example
 * ```js
 * let chessBoard = new ChessBuilder().default();
 * ```
 * @param {string} color currently color, w or b
 * @param {HTMLElement} statusControl status control element
 * @param {HTMLElement} roleControl role control element
 */
function ChessBuilder(color, statusControl, roleControl) {
  this.onDragStart = undefined;
  this.onDrop = undefined;
  this.onSnapEnd = undefined;

  /**
   * use default configuration to build a chess board
   * @returns AnduinChessBoard
   */
  this.default = () => {
    const anduinChessBoard = new AnduinChessBoard(color);
    anduinChessBoard.config.onDragStart = buildOnDragStart(anduinChessBoard);
    anduinChessBoard.config.onDrop = buildOnDrop(anduinChessBoard);
    anduinChessBoard.config.onSnapEnd = buildOnSnapEnd(anduinChessBoard);
    anduinChessBoard.statusControl = statusControl;
    anduinChessBoard.roleControl = roleControl;

    return anduinChessBoard;
  };
}

function AnduinChessBoard(color) {
  this.color = color;
  this.game = null;
  this.board = null;
  this.socket = null;
  this.statusControl = null;
  this.roleControl = null;
  this.lastMovePair = [null, null];
  this.isWhiteCheck = false;
  this.isBlackCheck = false;
  this.statusText = "";

  this.config = {
    orientation: this.color === BLACK_ABBREVIATION ? "black" : "white",
    draggable: true,
    dragoffBoard: "snapback",
    position: null,
    onDragStart: null,
    onDrop: null,
    onSnapEnd: null,
  };

  /**
   * render some styles, like highlight, red light
   */
  this.render = () => {
    this.renderTrack();
    this.renderCheck();
    this.renderStatusText();
  };

  this.renderTrack = () => {
    if (this.lastMovePair[0] !== null && this.lastMovePair[1] !== null) {
      let allSquares = Array.from(
        document.querySelectorAll(`#board [data-square]`)
      );
      allSquares
        .filter((square) => {
          let sq = square.getAttribute("data-square");
          return sq !== this.lastMovePair[0] && sq !== this.lastMovePair[1];
        })
        .forEach((square) => (square.style.boxShadow = ""));

      allSquares
        .filter((square) => {
          let sq = square.getAttribute("data-square");
          return sq === this.lastMovePair[0] || sq === this.lastMovePair[1];
        })
        .forEach(
          (square) =>
            (square.style.boxShadow = "inset .2px .2px 4px 4px #f9ff49")
        );
    }
  };

  this.renderCheck = () => {
    let checkedPosition = null;
    if (this.isWhiteCheck) {
      checkedPosition = this.getKingPosition(WHITE_ABBREVIATION);
    } else if (this.isBlackCheck) {
      checkedPosition = this.getKingPosition(BLACK_ABBREVIATION);
    }

    document.querySelectorAll("#board [data-square]").forEach((p) => {
      p.style.backgroundImage = "";
    });
    if (checkedPosition !== null) {
      document.querySelector(
        `#board [data-square=${checkedPosition}]`
      ).style.backgroundImage = "radial-gradient(circle, red 5%, transparent)";
    }
  };

  this.renderStatusText = () => {
    this.statusControl.innerHTML = this.statusText;
  };

  this.getKingPosition = (bOrW) => {
    let pieces = []
      .concat(...this.game.board())
      .filter((p) => p !== null && p.type === "k" && p.color === bOrW)
      .map((p) => p.square);

    return pieces.length > 0 ? pieces[0] : null;
  };

  this.run = (player, gameId) => {
    this.roleControl.innerHTML = `You are ${
      this.color === WHITE_ABBREVIATION
        ? "White"
        : this.color === BLACK_ABBREVIATION
        ? "Black"
        : "Spectator"
    } player.`;

    const completeInit = (fen) => {
      this.config.position = fen;

      const wsScheme =
        window.location.protocol === "https:" ? "wss://" : "ws://";
      this.socket = new WebSocket(
        `${wsScheme}${window.location.host}/games/${gameId}.ws?playerId=${player}`
      );

      this.board = ChessBoard("board", this.config);

      this.socket.onmessage = (event) => {
        this.refresh(event.data);
      };
      this.socket.onclose = () => {
        setTimeout(() => {
          this.run(player, gameId);
        }, 1000);
      };

      this.refresh(fen);
    };

    fetch(`/games/${gameId}.fen`)
      .then((response) => response.text())
      .then(completeInit);
  };

  this.refresh = (newFEN) => {
    if (this.game !== null) {
      let [position1, position2] = findMove(this.game.fen(), newFEN);
      this.lastMovePair = [position1, position2];
    }

    this.game = new Chess(newFEN);
    this.board.position(newFEN);
    console.log(`Got fen ${newFEN}. refreshing board...`);

    this.updateStatus();
    this.render();
  };

  this.updateStatus = () => {
    let moveColor = WHITE;
    if (this.game.turn() === BLACK_ABBREVIATION) {
      moveColor = BLACK;
    }

    this.isWhiteCheck = null;
    this.isBlackCheck = null;

    this.statusText = `${moveColor} to move`;

    if (this.game.isCheck()) {
      this.isWhiteCheck = moveColor === WHITE;
      this.isBlackCheck = moveColor === BLACK;
      this.statusText += `, ${moveColor} is in check`;
    }

    if (this.game.isCheckmate()) {
      this.isWhiteCheck = moveColor === WHITE;
      this.isBlackCheck = moveColor === BLACK;
      this.statusText = `Game over, ${moveColor} is in checkmate, and winner is ${
        this.game.turn() === WHITE_ABBREVIATION ? "Black" : "White"
      }`;
    }

    if (this.game.isDraw()) {
      this.statusText = "Game over, drawn position";
    }
  };
}

export default ChessBuilder;
