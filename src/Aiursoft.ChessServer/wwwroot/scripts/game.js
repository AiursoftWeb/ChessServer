const { default: ChessBuilder } = await import(document.querySelector('[data-chess-module=board]').href);
const id = document.getElementById('game-room').dataset.gameId;
const text = key => document.querySelector(`#chess-loc [data-key="${key}"]`).textContent;
const [colorResponse, infoResponse] = await Promise.all([fetch(`/games/${id}.color`), fetch(`/games/${id}.json`)]);
let board, color, state, sampledAt, polling = false, saving = false, saved = false, stopped = false;
const clockTop = document.getElementById('clock-top');
const clockBottom = document.getElementById('clock-bottom');
function renderClocks() {
    if (!state) return;
    const elapsed = performance.now() - sampledAt;
    for (const [element, side] of [[clockTop, color === 'b' ? 'w' : 'b'], [clockBottom, color === 'b' ? 'b' : 'w']]) {
        const active = !state.Game.Ended && state.Game.Turn === side;
        element.classList.toggle('chess-clock-active', active);
        element.querySelector('.clock-name').textContent = side === 'w' ? state.WhitePlayer : state.BlackPlayer;
        const initial = side === 'w' ? state.WhiteMilliseconds : state.BlackMilliseconds;
        const seconds = Math.ceil(Math.max(0, initial - (active ? elapsed : 0)) / 1000);
        element.querySelector('.clock-time').textContent = state.Timed ? `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}` : text('unlimited');
    }
}
async function saveGame() {
    if (color === 'm' || saving || saved) return;
    saving = true;
    const status = document.getElementById('save-status');
    document.getElementById('retry-save').hidden = true;
    status.textContent = text('saving');
    try {
        const response = await fetch(`/games/${id}.pgn`, {cache: 'no-store'});
        if (!response.ok) throw new Error('PGN unavailable');
        const form = new FormData(document.getElementById('complete-game'));
        form.set('pgn', await response.text());
        const result = await fetch(`/games/${id}/complete`, {method: 'POST', body: form});
        if (!result.ok) throw new Error('Save failed');
        const record = await result.json();
        saved = true;
        status.textContent = text('saved');
        const link = document.getElementById('history-link');
        link.href = `/History/Replay/${record.HistoryId}`;
        link.hidden = false;
    } catch {
        status.textContent = text('save-failed');
        document.getElementById('retry-save').hidden = false;
    } finally { saving = false; }
}
function applyState(info) {
    state = info;
    sampledAt = performance.now();
    renderClocks();
    if (!board) return;
    board.serverEnded = info.Game.Ended;
    if (info.Game.Ended) {
        const winner = info.Result === '1-0' ? text('white') : text('black');
        board.statusText = info.Result === '1/2-1/2' ? text('draw') : text(info.EndReason === 'Timeout' ? 'timeout' : 'finished').replace('{0}', winner);
        board.render();
        if (!saved && !saving && document.getElementById('retry-save').hidden) saveGame();
    }
}
async function refreshState() {
    if (polling || stopped) return;
    polling = true;
    try {
        const response = await fetch(`/games/${id}.json`, {cache: 'no-store'});
        if (response.ok && !stopped) applyState(await response.json());
    } finally { polling = false; }
}
if (colorResponse.ok && infoResponse.ok) {
    color = await colorResponse.text();
    const info = await infoResponse.json();
    document.getElementById('versus-info').textContent = `${info.Creator} vs ${info.Accepter}`;
    board = new ChessBuilder(color, document.getElementById('status'), document.getElementById('role'), document.getElementById('pieceMoveSound')).default();
    window.addEventListener('chess-position', () => refreshState().catch(() => {}));
    board.run(id);
    applyState(info);
    const poll = setInterval(() => { if (!state?.Game.Ended) refreshState().catch(() => {}); }, 1000);
    const render = setInterval(renderClocks, 100);
    document.getElementById('retry-save').onclick = saveGame;
    window.addEventListener('pagehide', () => { stopped = true; clearInterval(poll); clearInterval(render); board.dispose(); }, {once: true});
}
const messages = document.getElementById('messages-box');
const input = document.getElementById('chat-input');
const socket = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/chats/${id}.ws`);
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    const item = document.createElement('div');
    item.className = 'chess-message';
    const sender = document.createElement('strong'); sender.textContent = message.SenderNickName;
    const body = document.createElement('p'); body.textContent = message.Content;
    item.append(sender, body); messages.append(item);
    while (messages.children.length > 200) messages.firstElementChild.remove();
    messages.scrollTop = messages.scrollHeight;
};
document.getElementById('chat-form').onsubmit = event => {
    event.preventDefault();
    if (socket.readyState === WebSocket.OPEN && input.value.trim()) { socket.send(input.value); input.value = ''; }
};
socket.onclose = () => { input.disabled = true; document.getElementById('chat-status').textContent = document.querySelector('#chess-loc [data-key=closed]').textContent; };
window.addEventListener('pagehide', () => socket.close(), { once: true });
