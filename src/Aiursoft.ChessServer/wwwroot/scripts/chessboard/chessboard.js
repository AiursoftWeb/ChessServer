const { Chess } = await import(document.querySelector('[data-chess-module=rules]').href);
const {
  initSoundForMobile,
  initSoundForPC,
  playSoundForPC,
  playSoundForMobile,
} = await import(document.querySelector('[data-chess-module=sound]').href);
const {
  buildOnDragStart,
  buildOnDrop,
  buildOnSnapEnd,
  buildOnChange,
  WHITE_ABBREVIATION,
  BLACK_ABBREVIATION,
  WHITE,
  BLACK,
  findMove,
} = await import(document.querySelector('[data-chess-module=config]').href);

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
function ChessBuilder(color, statusControl, roleControl, soundControl) {
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
    anduinChessBoard.config.onChange = buildOnChange(anduinChessBoard);
    anduinChessBoard.statusControl = statusControl;
    anduinChessBoard.roleControl = roleControl;
    anduinChessBoard.soundControl = soundControl;

    return anduinChessBoard;
  };
}

function AnduinChessBoard(color) {
  const text = (key, fallback) => document.querySelector(`#chess-loc [data-key="${key}"]`)?.textContent ?? fallback;
  const format = (key, fallback, value) => text(key, fallback).replace('{0}', () => value);
  const colorName = color => color === WHITE_ABBREVIATION ? text('white', 'White') : text('black', 'Black');
  this.color = color;
  this.game = null;
  this.board = null;
  this.socket = null;
  this.statusControl = null;
  this.roleControl = null;
  this.soundControl = null;
  this.lastMovePair = [null, null];
  this.isWhiteCheck = false;
  this.isBlackCheck = false;
  this.statusText = "";

  this.config = {
    pieceTheme: "/img/chesspieces/wikipedia/{piece}.png",
    orientation: this.color === BLACK_ABBREVIATION ? "black" : "white",
    draggable: true,
    dragoffBoard: "snapback",
    position: null,
    onDragStart: null,
    onDrop: null,
    onSnapEnd: null,
  };

  this._initSound = () => {
    if (/Mobile/.test(navigator.userAgent)) {
      initSoundForMobile.bind(this)(this);
      this._playSound = playSoundForMobile.bind(this);
    } else {
      initSoundForPC.bind(this)(this);
      this._playSound = playSoundForPC.bind(this);
    }
  };

  this._playSound = () => {};

  /**
   * render some styles, like highlight, red light
   */
  this.render = () => {
    this._renderTrack();
    this._renderCheck();
    this._renderStatusText();
  };

  this._renderTrack = () => {
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

  this._renderCheck = () => {
    let checkedPosition = null;
    if (this.isWhiteCheck) {
      checkedPosition = this._getKingPosition(WHITE_ABBREVIATION);
    } else if (this.isBlackCheck) {
      checkedPosition = this._getKingPosition(BLACK_ABBREVIATION);
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

  this._renderStatusText = () => {
    this.statusControl.textContent = this.statusText;
  };

  this._getKingPosition = (bOrW) => {
    let pieces = []
      .concat(...this.game.board())
      .filter((p) => p !== null && p.type === "k" && p.color === bOrW)
      .map((p) => p.square);

    return pieces.length > 0 ? pieces[0] : null;
  };

  this.disposed = false;
  this.retries = 0;
  this.reconnectTimer = null;
  let previousWidth = 0;
  this.resizeObserver = new ResizeObserver(entries => {
    const width = entries[0].contentRect.width;
    if (width !== previousWidth) {
      previousWidth = width;
      this.board?.resize();
    }
  });
  this.resizeObserver.observe(document.getElementById('board').parentElement);
  this.dispose = () => {
    this.disposed = true;
    clearTimeout(this.reconnectTimer);
    this.resizeObserver.disconnect();
    this.socket?.close();
    this.board?.destroy();
  };
  this.run = (gameId) => {
    this.roleControl.textContent = this.color === 'm' ? text('watching', 'You are watching this game.') : this.color === WHITE_ABBREVIATION ? text('playing-white', 'You are playing White.') : text('playing-black', 'You are playing Black.');

    this._initSound();

    const completeInit = (fen) => {
      this.config.position = fen;

      const wsScheme =
        window.location.protocol === "https:" ? "wss://" : "ws://";
      this.socket = new WebSocket(
        `${wsScheme}${window.location.host}/games/${gameId}.ws`
      );

      this.board?.destroy();
      this.board = ChessBoard("board", this.config);

      this.socket.onmessage = (event) => {
        this._refresh(event.data);
      };
      this.socket.onclose = () => {
        if (!this.disposed && this.retries++ < 5) {
          this.reconnectTimer = setTimeout(() => this.run(gameId), 1000 * this.retries);
        } else {
          this.statusControl.textContent = text('closed', 'Connection closed. Refresh to reconnect.');
        }
      };

      this._refresh(fen);
    };

    fetch(`/games/${gameId}.fen`)
      .then((response) => { if (!response.ok) throw new Error('Room unavailable'); return response.text(); })
      .then(fen => { if (!this.disposed) completeInit(fen); })
      .catch(() => { this.statusControl.textContent = text('unavailable', 'Room unavailable. Return to the lobby.'); });
  };

  this._refresh = (newFEN) => {
    this.latestFen = newFEN;
    if (this.game !== null) {
      let [position1, position2] = findMove(this.game.fen(), newFEN);
      this.lastMovePair = [position1, position2];
    }

    this.game = new Chess(newFEN);
    this.board.position(newFEN);
    console.log(`Got fen ${newFEN}. refreshing board...`);

    this._updateStatus();
    this.render();
    window.dispatchEvent(new Event('chess-position'));
  };

  this._updateStatus = () => {
    let moveColor = WHITE;
    if (this.game.turn() === BLACK_ABBREVIATION) {
      moveColor = BLACK;
    }

    this.isWhiteCheck = null;
    this.isBlackCheck = null;

    this.statusText = format('turn', '{0} to move', colorName(this.game.turn()));

    if (this.game.isCheck()) {
      this.isWhiteCheck = moveColor === WHITE;
      this.isBlackCheck = moveColor === BLACK;
      this.statusText += ` — ${format('check', '{0} is in check', colorName(this.game.turn()))}`;
    }

    if (this.game.isCheckmate()) {
      this.isWhiteCheck = moveColor === WHITE;
      this.isBlackCheck = moveColor === BLACK;
      this.statusText = format('winner', 'Checkmate. {0} wins.', colorName(this.game.turn() === WHITE_ABBREVIATION ? BLACK_ABBREVIATION : WHITE_ABBREVIATION));
    }

    if (this.game.isDraw()) {
      this.statusText = text("draw", "Game over — draw.");
    }
  };
}

export default ChessBuilder;
