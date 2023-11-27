import {Chess} from "/chess.js/dist/esm/chess.js";

const initGameBoard = function (player, gameId) {
    $.get("/games/" + gameId + "/fen", function (fen) {
        let board = null
        let game = new Chess(fen);
        const refreshButton = $('#refresh');
        const statusControl = $('#status');
        const fenControl = $('#fen');
        const pgnControl = $('#pgn');

        function refresh(newFen) {
            game = new Chess(newFen);
            board.position(newFen);
            updateStatus();
        }

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

        // Happens when a player drops a piece.
        function onDrop(source, target) {
            try {
                // see if the move is legal
                const move = game.move({
                    from: source,
                    to: target,
                    promotion: 'q' // NOTE: always promote to a queen for example simplicity
                })

                if (move === null) {
                    return 'snapback'
                }

                // Get the last move and send it to server.
                const lastMove = game.history({verbose: true}).pop().san;
                $.post("/games/" + gameId + "/move/" + player + "/" + lastMove, function (fen) {
                    refresh(fen);
                });
            } catch (e) {
                return 'snapback'
            }
        }

        // Hack to make sure castling\promotion works.
        function onSnapEnd() {
            board.position(game.fen())
        }

        function updateStatus() {
            let status;
            let moveColor = 'White';
            if (game.turn() === 'b') {
                moveColor = 'Black'
            }

            // checkmate?
            if (game.isCheckmate()) {
                status = 'Game over, ' + moveColor + ' is in checkmate.'
            }

            // draw?
            else if (game.isDraw()) {
                status = 'Game over, drawn position'
            }

            // game still on
            else {
                status = moveColor + ' to move'

                // check?
                if (game.isCheck()) {
                    status += ', ' + moveColor + ' is in check'
                }
            }

            statusControl.html(status)
            fenControl.html(game.fen())
            pgnControl.html(game.pgn())
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

        // Bind Refresh button.
        refreshButton.click(function () {
            $.get("/games/" + gameId + "/fen", function (fen) {
                refresh(fen);
            });
        });

        updateStatus();
    });

};

// noinspection JSUnusedGlobalSymbols
export default initGameBoard;