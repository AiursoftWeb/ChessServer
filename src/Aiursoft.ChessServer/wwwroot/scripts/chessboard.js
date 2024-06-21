import { Chess } from "../node_modules/chess.js/dist/esm/chess.js";

const initGameBoard = function (color, player, gameId) {
    fetch(`/games/${gameId}.fen`)
        .then(response => response.text())
        .then(fen => {
            let board = null;
            let game = null;
            let whiteSquareGrey = '#a9a9a9';
            let blackSquareGrey = '#696969';
            const wsScheme = window.location.protocol === "https:" ? "wss://" : "ws://";
            const socket = new WebSocket(
                `${wsScheme}${window.location.host}/games/${gameId}.ws?playerId=${player}`
            );

            function removeGreySquares() {
                $('#board .square-55d63').css('background', '');
            }

            function greySquare(square) {
                var $square = $('#board .square-' + square);

                var background = whiteSquareGrey;
                if ($square.hasClass('black-3c85d')) {
                    background = blackSquareGrey;
                }

                $square.css('background', background);
            }

            function onDragStart(square, piece, position, _) {
                if (game.turn() !== color) {
                    return false;
                }

                if (game.isGameOver()) return false;

                if (
                    (game.turn() === "w" && piece.search(/^b/) !== -1) ||
                    (game.turn() === "b" && piece.search(/^w/) !== -1)) {
                    return false
                }

                const moves = game.moves({
                    square: square,
                    verbose: true
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
                    const lastMove = game.history({verbose: true}).pop().san;
                    socket.send(lastMove);
                } catch (e) {
                    return "snapback";
                }
            }

            function onSnapEnd() {
                board.position(game.fen());
            }

            function onMouseoutSquare(square, piece) {
                removeGreySquares();
            }

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
                orientation: color === "w" ? "white" : color === "b" ? "black" : /*spectator*/ "white",
                draggable: true,
                dragoffBoard: "snapback",
                position: fen,
                onDragStart: onDragStart,
                onSnapEnd: onSnapEnd,
                onDrop: onDrop,
                onMouseoutSquare: onMouseoutSquare
            };
            board = ChessBoard("board", config);
            const statusControl = document.getElementById("status");
            const roleControl = document.getElementById("role");
            roleControl.innerHTML = `You are ${color === "w" ? "White" : color === "b" ? "Black" : "Spectator"} player.`;

            function refresh(newFen) {
                game = new Chess(newFen);
                board.position(newFen);
                console.log(`Got fen ${newFen}. refreshing board...`);
                updateStatusText();
            }

            refresh(fen);

            socket.onmessage = function (event) {
                refresh(event.data);
            };

            socket.onclose = function () {
                setTimeout(function () {
                    initGameBoard(color, player, gameId);
                }, 1000);
            };
        });
};

export default initGameBoard;
