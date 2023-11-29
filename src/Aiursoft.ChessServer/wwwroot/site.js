import { Chess } from "/node_modules/chess.js/dist/esm/chess.js";

const statusControl = $('#status');
const fenControl = $('#fen');

const initGameBoard = function (player, gameId) {
    $.get("/games/" + gameId + ".fen", function (fen) {
        let board = null;
        let game = null;

        // Happens when a player picks up a piece.
        function onDragStart(source, piece, position, _) {
            // only allow moving players own pieces
            if ((game.turn() !== player)) {
                return false
            }

            // do not pick up pieces if the game is over
            if (game.isGameOver()) return false

            // only pick up pieces for the side to move
            if ((game.turn() === 'w' && piece.search(/^b/) !== -1) ||
                (game.turn() === 'b' && piece.search(/^w/) !== -1)) {
                return false
            }
        }

        function onDrop(source, target) {
            try {
                const move = game.move({
                    from: source,
                    to: target,
                    promotion: 'q' 
                })
                if (move === null) {
                    return 'snapback'
                }
                const lastMove = game.history({verbose: true}).pop().san;
                $.post("/games/" + gameId + "/move/" + player + "/" + lastMove);
            } catch (e) {
                return 'snapback';
            }
        }

        // Hack to make sure castling\promotion works.
        function onSnapEnd() {
            board.position(game.fen())
        }

        function updateStatusText() {
            let status;
            let moveColor = 'White';
            if (game.turn() === 'b') {
                moveColor = 'Black';
            } if (game.isCheckmate()) {
                status = 'Game over, ' + moveColor + ' is in checkmate, and winner is ' + (game.turn() === 'w' ? 'Black' : 'White');
            } else if (game.isDraw()) {
                status = 'Game over, drawn position';
            } else {
                status = moveColor + ' to move';
                if (game.isCheck()) {
                    status += ', ' + moveColor + ' is in check';
                }
            }
            statusControl.html(status);
            fenControl.html(game.fen());
        }

        const config = {
            orientation: player === "w" ? "white" : "black",
            draggable: true,
            dragoffBoard: 'snapback',
            position: fen,
            onDragStart: onDragStart,
            onSnapEnd: onSnapEnd,
            onDrop: onDrop
        };
        board = ChessBoard('board', config);

        function refresh(newFen) {
            game = new Chess(newFen);
            board.position(newFen);
            updateStatusText();
        }

        refresh(fen);
        
        const wsScheme = window.location.protocol === "https:" ? "wss://" : "ws://";
        const socket = new WebSocket(wsScheme + window.location.host + "/games/" + gameId + ".ws");
        socket.onmessage = function (event) {
            refresh(event.data);
        };
        
        // Auto reconnect.
        socket.onclose = function () {
            alert("Socket closed. Reconnecting...");
            setTimeout(function () {
                initGameBoard(player, gameId);
            }, 1000);
        };
    });

};

// noinspection JSUnusedGlobalSymbols
export default initGameBoard;