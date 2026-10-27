const root = document.getElementById('replay');
const controls = ['start', 'prev', 'next', 'end'].map(name => document.getElementById(`replay-${name}`));
controls.forEach(button => { button.disabled = true; });
try {
    const {Chess} = await import(document.querySelector('[data-chess-module=rules]').href);
    const response = await fetch(root.dataset.pgnUrl, {cache: 'no-store'});
    if (!response.ok) throw new Error('PGN unavailable');
    const game = new Chess();
    game.loadPgn(await response.text());
    const moves = game.history();
    const replay = new Chess();
    const positions = [replay.fen()];
    for (const move of moves) { replay.move(move); positions.push(replay.fen()); }
    const board = ChessBoard('board', {position: 'start', draggable: false, orientation: root.dataset.orientation, pieceTheme: '/img/chesspieces/wikipedia/{piece}.png'});
    let index = 0;
    function show(next) {
        index = Math.max(0, Math.min(positions.length - 1, next));
        board.position(positions[index]);
        document.getElementById('replay-position').textContent = `${index} / ${moves.length}`;
        controls[0].disabled = controls[1].disabled = index === 0;
        controls[2].disabled = controls[3].disabled = index === moves.length;
    }
    controls[0].onclick = () => show(0);
    controls[1].onclick = () => show(index - 1);
    controls[2].onclick = () => show(index + 1);
    controls[3].onclick = () => show(moves.length);
    const observer = new ResizeObserver(() => board.resize());
    observer.observe(root);
    window.addEventListener('pagehide', () => { observer.disconnect(); board.destroy(); }, {once: true});
    show(0);
} catch { document.getElementById('replay-error').hidden = false; }
