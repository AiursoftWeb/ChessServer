const {Chess} = await import(document.querySelector('[data-chess-module=rules]').href);

export default class ChessSurface {
    constructor(color, sendMove) {
        this.color = color;
        this.sendMove = sendMove;
        this.state = null;
        this.pending = false;
        this.viewPly = 0;
        this.positions = [];
        this.moves = [];
        this.selected = null;
        this.suppressClickUntil = 0;
        this.board = ChessBoard('board', {
            position: 'start', orientation: color === 'b' ? 'black' : 'white', draggable: color !== 'm',
            pieceTheme: '/img/chesspieces/wikipedia/{piece}.png',
            onDragStart: (_, piece) => this.canMove() && piece[0].toLowerCase() === color,
            onDrop: (source, target) => { if (source === target) return 'snapback'; this.suppressClickUntil = performance.now() + 350; this.attempt(source, target); return 'snapback'; },
            onSnapbackEnd: () => this.paint(), onSnapEnd: () => this.paint()
        });
        this.pointerDown = event => {
            const square = event.target.closest('[data-square]')?.dataset.square;
            if (square) this.pointerStart = {square, x: event.clientX, y: event.clientY};
        };
        this.pointerUp = event => {
            const start = this.pointerStart; this.pointerStart = null;
            if (!start || Math.hypot(event.clientX - start.x, event.clientY - start.y) > 8 || !this.canMove()) return;
            const square = start.square, piece = this.game.get(square);
            if (this.selected && square !== this.selected && piece?.color !== this.color) {
                this.attempt(this.selected, square); this.selected = null;
            } else this.selected = piece?.color === this.color && this.selected !== square ? square : null;
            this.highlight();
        };
        document.getElementById('board').addEventListener('pointerdown', this.pointerDown, true);
        document.addEventListener('pointerup', this.pointerUp, true);
        this.observer = new ResizeObserver(() => this.board.resize());
        this.observer.observe(document.getElementById('board').parentElement);
        document.getElementById('return-live').onclick = () => this.show(this.moves.length);
    }
    canMove() {
        return this.state && !this.state.Game.Ended && this.state.Game.Turn === this.color && !this.pending && this.viewPly === this.moves.length;
    }
    update(state) {
        if (this.state && this.state.Position !== state.Position && document.getElementById('promotion-dialog').open) document.getElementById('promotion-dialog').close();
        const wasLive = this.viewPly === this.moves.length;
        this.state = state;
        this.game = new Chess(state.Position);
        if (this.moves.join(' ') !== state.Moves.join(' ') || this.positions.length === 0) {
            this.moves = state.Moves.slice();
            const replay = new Chess(); this.positions = [replay.fen()]; this.moveSquares = [];
            for (const move of this.moves) { const played = replay.move(move); this.moveSquares.push(played); this.positions.push(replay.fen()); }
            this.buildList();
        }
        if (wasLive) this.viewPly = this.moves.length;
        this.paint();
    }
    buildList() {
        const list = document.getElementById('move-list'); list.replaceChildren();
        this.moves.forEach((move, index) => {
            if (index % 2 === 0) { const label = document.createElement('span'); label.className = 'small text-muted'; label.textContent = `${index / 2 + 1}.`; list.append(label); }
            const button = document.createElement('button'); button.className = 'btn btn-sm btn-light'; button.type = 'button';
            button.dataset.ply = String(index + 1); button.textContent = move; button.onclick = () => this.show(index + 1); list.append(button);
        });
        list.scrollTop = list.scrollHeight;
    }
    show(ply) { this.viewPly = ply; this.selected = null; this.paint(); }
    paint() {
        if (!this.state) return;
        this.board.position(this.viewPly === this.moves.length ? this.state.Position : this.positions[this.viewPly], false);
        document.getElementById('return-live').hidden = this.viewPly === this.moves.length;
        const buttons = document.querySelectorAll('#move-list button');
        buttons.forEach(button => { button.classList.toggle('btn-primary', Number(button.dataset.ply) === this.viewPly); button.classList.toggle('btn-light', Number(button.dataset.ply) !== this.viewPly); });
        this.highlight();
    }
    highlight() {
        document.querySelectorAll('#board [data-square]').forEach(element => element.classList.remove('chess-selected', 'chess-legal', 'chess-last', 'chess-check'));
        const last = this.moveSquares?.[this.viewPly - 1];
        if (last) for (const square of [last.from, last.to]) document.querySelector(`#board [data-square="${square}"]`)?.classList.add('chess-last');
        if (this.game) {
            const shown = this.viewPly === this.moves.length ? this.game : new Chess(this.positions[this.viewPly]);
            if (shown.isCheck()) {
                const king = shown.board().flat().find(piece => piece?.type === 'k' && piece.color === shown.turn());
                document.querySelector(`#board [data-square="${king.square}"]`)?.classList.add('chess-check');
            }
        }
        if (!this.selected || !this.game) return;
        document.querySelector(`#board [data-square="${this.selected}"]`)?.classList.add('chess-selected');
        for (const move of this.game.moves({square: this.selected, verbose: true})) document.querySelector(`#board [data-square="${move.to}"]`)?.classList.add('chess-legal');
    }
    async promotion() {
        const dialog = document.getElementById('promotion-dialog');
        return new Promise(resolve => {
            let chosen = null;
            const choose = event => { const piece = event.target.closest('[data-promotion]')?.dataset.promotion; if (piece) { chosen = piece; dialog.close(); } };
            const cancel = () => dialog.close();
            dialog.addEventListener('click', choose); document.getElementById('cancel-promotion').onclick = cancel;
            dialog.addEventListener('close', () => { dialog.removeEventListener('click', choose); resolve(chosen); }, {once: true});
            dialog.showModal();
        });
    }
    async attempt(source, target) {
        if (!this.canMove()) return;
        const position = this.state.Position;
        const candidates = this.game.moves({square: source, verbose: true}).filter(move => move.to === target);
        if (candidates.length === 0) return;
        let promotion;
        if (candidates.some(move => move.promotion)) promotion = await this.promotion();
        if (candidates.some(move => move.promotion) && !promotion || !this.canMove() || this.state.Position !== position) return;
        const local = new Chess(position);
        const move = local.move({from: source, to: target, ...(promotion ? {promotion} : {})});
        if (move) { this.pending = true; this.selected = null; this.sendMove(move.san, this.moves.length); }
    }
    dispose() {
        document.getElementById('board').removeEventListener('pointerdown', this.pointerDown, true);
        document.removeEventListener('pointerup', this.pointerUp, true);
        this.observer.disconnect(); this.board.destroy();
    }
}
