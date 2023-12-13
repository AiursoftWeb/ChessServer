﻿import { Chess } from "../node_modules/chess.js/dist/esm/chess.js";

const initGameBoard = function (player, gameId) {
  fetch(`/games/${gameId}.fen`)
      .then(response => response.text())
      .then(fen => {
        let board = null;
        let game = null;

        function onDragStart(source, piece, position, _) {
          if (game.turn() !== player) {
            return false;
          }

          if (game.isGameOver()) return false;

          if (
              (game.turn() === "w" && piece.search(/^b/) !== -1) ||
              (game.turn() === "b" && piece.search(/^w/) !== -1)) {
            return false
          }
        }

        function onDrop(source, target) {
          try {
            const move = game.move({
              from: source,
              to: target,
              promotion: "q",
            });
            if (move === null) {
              return "snapback";
            }
            const lastMove = game.history({ verbose: true }).pop().san;
            fetch(`/games/${gameId}/move/${player}/${lastMove}`, { method: 'POST' });
          } catch (e) {
            return "snapback";
          }
        }

        function onSnapEnd() {
          board.position(game.fen());
        }

        const statusControl = document.getElementById("status");
        function updateStatusText() {
          let status;
          let moveColor = "White";
          if (game.turn() === "b") {
            moveColor = "Black";
          }
          if (game.isCheckmate()) {
            status = `Game over, ${moveColor} is in checkmate, and winner is ${game.turn() === "w" ? "Black" : "White"}`;
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
          orientation: player === "w" ? "white" : "black",
          draggable: true,
          dragoffBoard: "snapback",
          position: fen,
          onDragStart: onDragStart,
          onSnapEnd: onSnapEnd,
          onDrop: onDrop
        };
        board = ChessBoard("board", config);

        function refresh(newFen) {
          game = new Chess(newFen);
          board.position(newFen);
          console.log(`Got fen ${newFen}. refreshing board...`);
          updateStatusText();
        }

        refresh(fen);

        const wsScheme = window.location.protocol === "https:" ? "wss://" : "ws://";
        const socket = new WebSocket(
            `${wsScheme}${window.location.host}/games/${gameId}.ws`
        );
        socket.onmessage = function (event) {
          refresh(event.data);
        };

        socket.onclose = function () {
          setTimeout(function () {
            initGameBoard(player, gameId);
          }, 1000);
        };
      });
};

export default initGameBoard;
