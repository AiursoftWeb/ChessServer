import { Chess } from "../../node_modules/chess.js/dist/esm/chess.js";

import {
  buildOnDragStart,
  buildOnDrop,
  buildOnSnapEnd,
  WHITE_ABBREVIATION,
  BLACK_ABBREVIATION,
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

  this.config = {
    orientation: this.color === BLACK_ABBREVIATION ? "black" : "white",
    draggable: true,
    dragoffBoard: "snapback",
    position: null,
    onDragStart: null,
    onDrop: null,
    onSnapEnd: null,
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
    this.game = new Chess(newFEN);
    this.board.position(newFEN);
    console.log(`Got fen ${newFEN}. refreshing board...`);

    this.updateStatusText();
  };

  this.updateStatusText = () => {
    let status;
    let moveColor = "White";
    if (this.game.turn() === BLACK_ABBREVIATION) {
      moveColor = "Black";
    }
    if (this.game.isCheckmate()) {
      status = `Game over, ${moveColor} is in checkmate, and winner is ${
        this.game.turn() === WHITE_ABBREVIATION ? "Black" : "White"
      }`;
    } else if (this.game.isDraw()) {
      status = "Game over, drawn position";
    } else {
      status = `${moveColor} to move`;
      if (this.game.isCheck()) {
        status += `, ${moveColor} is in check`;
      }
    }
    this.statusControl.innerHTML = status;
  };
}

export default ChessBuilder;
