const {default: ChessSurface} = await import(document.querySelector('[data-chess-module=board]').href);
const root = document.getElementById('game-room');
const id = root.dataset.gameId;
const text = key => document.querySelector(`#chess-loc [data-key="${key}"]`)?.textContent ?? key;
const format = (key, value) => text(key).replace('{0}', value);
const element = id => document.getElementById(id);
const playerName = (name, guest) => guest ? `${name} (${text('guest')})` : name;
let state, color, surface, socket, stopped = false, sampledAt = 0, pending = null, retryTimer, polling = false, actionBusy = false, saving = false, saved = false, aiBusy = false;
let lastAiAttempt = 0, lastSend = 0, reconnectAttempts = 0;
const pendingKey = `chess-move-${id}`;
try { pending = JSON.parse(sessionStorage.getItem(pendingKey)); } catch { sessionStorage.removeItem(pendingKey); }
function clearPending() { pending = null; sessionStorage.removeItem(pendingKey); if (surface) surface.pending = false; }
function sendPending() {
    if (!pending || socket?.readyState !== WebSocket.OPEN) return;
    socket.send(JSON.stringify(pending)); lastSend = performance.now();
}
function sendMove(move, ply) {
    pending = {Id: crypto.randomUUID(), Kind: 'move', ExpectedPly: ply, Move: move, ExpectedRevision: state.Revision};
    sessionStorage.setItem(pendingKey, JSON.stringify(pending));
    sendPending(); render();
}
function apply(info) {
    if (!info || stopped) return;
    if (state && (info.Revision < state.Revision || info.Revision === state.Revision && info.SampledAt < state.SampledAt)) return;
    const justEnded = !state?.Game.Ended && info.Game.Ended;
    const acceptedRematch = !!state?.RematchOfferedBy && !state.NextGameId && !!info.NextGameId;
    const previousMoves = state?.Moves.length ?? 0;
    if (info.Moves.length < previousMoves) clearPending();
    state = info; sampledAt = performance.now();
    surface.update(info);
    if (info.Moves.length > previousMoves) element('pieceMoveSound').play().catch(() => {});
    element('versus-info').textContent = `${playerName(info.WhitePlayer, info.WhiteGuest)} — ${playerName(info.BlackPlayer, info.BlackGuest)}`;
    element('role').textContent = text(color === 'w' ? 'playing-white' : color === 'b' ? 'playing-black' : 'watching');
    if (info.Game.Ended) { if (element('promotion-dialog').open) element('promotion-dialog').close(); clearPending(); if (!saved && !saving && element('retry-save').hidden) saveGame(); }
    render();
    if (acceptedRematch && color !== 'm') location.assign(`/games/${info.NextGameId}.html`);
    if (justEnded) element('game-result').scrollIntoView({block: 'nearest', behavior: 'smooth'});
}
function render() {
    if (!state) return;
    const ended = state.Game.Ended;
    const elapsed = Math.max(0, performance.now() - sampledAt);
    for (const [clockId, side] of [['clock-top', color === 'b' ? 'w' : 'b'], ['clock-bottom', color === 'b' ? 'b' : 'w']]) {
        const clock = element(clockId), active = !ended && state.Game.Turn === side;
        clock.classList.toggle('chess-clock-active', active);
        clock.querySelector('.clock-name').textContent = side === 'w' ? playerName(state.WhitePlayer, state.WhiteGuest) : playerName(state.BlackPlayer, state.BlackGuest);
        const ms = side === 'w' ? state.WhiteMilliseconds : state.BlackMilliseconds;
        const seconds = Math.ceil(Math.max(0, ms - (active ? elapsed : 0)) / 1000);
        clock.querySelector('.clock-time').textContent = state.Timed ? `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}` : text('unlimited');
    }
    element('time-control').textContent = state.Timed ? format('time-control', `${state.InitialMinutes}+${state.IncrementSeconds}`) : text('unlimited');
    const participant = color !== 'm';
    element('game-actions').hidden = ended || !participant;
    element('abort').hidden = state.Moves.length !== 0;
    element('undo').hidden = !state.Computer || !participant;
    element('undo').disabled = !state.CanUndo || actionBusy || !!pending;
    element('offer-draw').hidden = state.Computer || !!state.DrawOfferedBy;
    element('resign-confirmation').hidden ||= ended;
    element('undo-confirmation').hidden ||= ended;
    const offeredByMe = state.DrawOfferedBy === (color === 'w' ? state.WhiteId : state.BlackId);
    element('draw-offer').hidden = ended || !state.DrawOfferedBy || !participant;
    element('draw-offer-text').textContent = text(offeredByMe ? 'own-offer' : 'their-offer');
    element('accept-draw').hidden = offeredByMe;
    const absent = color === 'w' ? state.BlackAbsentSeconds : state.WhiteAbsentSeconds;
    const offline = !state.Computer && participant && absent > 15 && !ended;
    element('opponent-status').hidden = !offline;
    element('opponent-status').textContent = absent >= 90 ? text('claim-ready') : format('opponent-offline', Math.ceil(90 - absent));
    element('claim').hidden = !offline || absent < 90;
    element('status').textContent = ended ? text('ended') : pending ? text('pending') : state.Computer && state.Game.Turn !== color && participant ? text('thinking') : format('turn', text(state.Game.Turn === 'w' ? 'white' : 'black'));
    element('game-result').hidden = !ended;
    element('game-policy').hidden = ended;
    element('offer-rematch').hidden = !participant || !!state.RematchOfferedBy || !!state.NextGameId;
    const ownRematch = state.RematchOfferedBy === (color === 'w' ? state.WhiteId : state.BlackId);
    element('rematch-offer').hidden = !ended || !participant || !state.RematchOfferedBy || !!state.NextGameId;
    element('rematch-offer-text').textContent = text(ownRematch ? 'own-rematch' : 'their-rematch');
    element('accept-rematch').hidden = ownRematch;
    element('next-game').hidden = !participant || !state.NextGameId;
    if (participant && state.NextGameId) element('next-game').href = `/games/${state.NextGameId}.html`;
    if (ended) {
        element('result-title').textContent = state.Result === '*' ? text(state.EndReason === 'Abandoned' ? 'Abandoned' : 'Aborted') : state.Result === '1/2-1/2' ? text('draw') : format('winner', text(state.Result === '1-0' ? 'white' : 'black'));
        const reason = text(state.EndReason);
        element('result-reason').textContent = reason === state.EndReason ? text('other-end') : reason;
    }
}
async function post(path, fields) {
    const form = new FormData(element('complete-game'));
    for (const [key, value] of Object.entries(fields)) form.set(key, value);
    return fetch(path, {method: 'POST', body: form});
}
async function saveGame() {
    if (color === 'm' || saving || saved) return;
    saving = true; element('retry-save').hidden = true; element('save-status').textContent = text('saving');
    try {
        const pgn = await fetch(`/games/${id}.pgn`, {cache: 'no-store'});
        if (!pgn.ok) throw Error('PGN unavailable');
        const response = await post(`/games/${id}/complete`, {pgn: await pgn.text()});
        if (!response.ok) throw Error('Save not confirmed');
        const info = await response.json(); saved = true; element('save-status').textContent = text(info.HistoryId ? 'saved' : 'guest-complete');
        if (info.HistoryId) { element('history-link').href = `/History/Replay/${info.HistoryId}`; element('history-link').hidden = false; }
    } catch { element('save-status').textContent = text('save-failed'); element('retry-save').hidden = false; }
    finally { saving = false; }
}
let pendingAction = null;
async function action(kind) {
    if (actionBusy || !state || state.Game.Ended && !kind.endsWith('rematch')) return;
    actionBusy = true;
    if (pendingAction?.kind !== kind) pendingAction = {kind, commandId: crypto.randomUUID(), expectedRevision: state.Revision};
    try {
        const response = await post(`/games/${id}/action`, pendingAction);
        const result = await response.json();
        apply(result.State ?? result);
        element('action-error').hidden = response.ok;
        if (!response.ok) element('action-error').textContent = text(result.Error);
        pendingAction = null; element('resign-confirmation').hidden = true; element('undo-confirmation').hidden = true;
        if (response.ok && state?.NextGameId && kind.endsWith('rematch')) location.assign(`/games/${state.NextGameId}.html`);
    } catch { element('action-error').textContent = text('error'); element('action-error').hidden = false; }
    finally { actionBusy = false; }
}
function connect() {
    if (stopped) return;
    element('connection-status').textContent = text(reconnectAttempts ? 'reconnecting' : 'connecting');
    socket = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/games/${id}.ws`);
    socket.onopen = () => { reconnectAttempts = 0; element('connection-status').textContent = text('connected'); sendPending(); };
    socket.onmessage = event => {
        let message;
        try { message = JSON.parse(event.data); } catch { return; }
        if (message.CommandId && message.CommandId === pending?.Id) {
            clearPending(); element('action-error').hidden = !message.Error;
            if (message.Error) element('action-error').textContent = text(message.Error);
        }
        apply(message.State ?? message);
    };
    socket.onclose = () => {
        if (stopped) return;
        element('connection-status').textContent = text('reconnecting');
        retryTimer = setTimeout(connect, Math.min(1000 * ++reconnectAttempts, 10000));
    };
}
async function poll() {
    if (polling || stopped) return;
    polling = true;
    try {
        const response = await fetch(`/games/${id}.json`, {cache: 'no-store'});
        if (response.ok) apply(await response.json());
        else if (response.status === 404) element('action-error').textContent = text('unavailable');
        if (pending && performance.now() - lastSend > 2000) sendPending();
        if (state?.Computer && !state.Game.Ended && color !== 'm' && state.Game.Turn !== color && !state.Thinking && !aiBusy && performance.now() - lastAiAttempt > 5000) {
            aiBusy = true; lastAiAttempt = performance.now();
            try { const wake = await post(`/games/${id}/computer`, {}); if (wake.ok) apply(await wake.json()); } finally { aiBusy = false; }
        }
    } catch { element('connection-status').textContent = text('reconnecting'); }
    finally { polling = false; }
}
try {
    const response = await fetch(`/games/${id}.color`);
    if (!response.ok) throw Error('Room unavailable');
    color = await response.text(); surface = new ChessSurface(color, sendMove); surface.pending = !!pending;
    await poll(); connect();
} catch { element('action-error').textContent = text('unavailable'); element('action-error').hidden = false; }
const heartbeat = setInterval(poll, 1000), clock = setInterval(render, 100);
element('resign').onclick = () => { element('resign-confirmation').hidden = false; };
element('cancel-resign').onclick = () => { element('resign-confirmation').hidden = true; };
element('abort').onclick = () => action('abort');
element('undo').onclick = () => { element('undo-confirmation').hidden = false; };
element('cancel-undo').onclick = () => { element('undo-confirmation').hidden = true; };
document.querySelectorAll('[data-game-action]').forEach(button => { button.onclick = () => action(button.dataset.gameAction); });
element('retry-save').onclick = saveGame;
window.addEventListener('beforeunload', event => { if (state && !state.Game.Ended && color !== 'm') { event.preventDefault(); event.returnValue = text('leave'); } });
window.addEventListener('pagehide', () => { stopped = true; clearInterval(heartbeat); clearInterval(clock); clearTimeout(retryTimer); socket?.close(); surface?.dispose(); }, {once: true});
if (element('chat-form')) {
    const messages = element('messages-box'), input = element('chat-input');
    const chat = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/chats/${id}.ws`);
    chat.onmessage = event => {
        const message = JSON.parse(event.data), item = document.createElement('div'); item.className = 'chess-message';
        const sender = document.createElement('strong'); sender.textContent = playerName(message.SenderNickName, message.SenderGuest);
        const body = document.createElement('p'); body.textContent = message.Content; item.append(sender, body); messages.append(item);
        while (messages.children.length > 200) messages.firstElementChild.remove(); messages.scrollTop = messages.scrollHeight;
    };
    element('chat-form').onsubmit = event => { event.preventDefault(); if (chat.readyState === WebSocket.OPEN && input.value.trim()) { chat.send(input.value); input.value = ''; } };
    chat.onclose = () => { input.disabled = true; element('chat-status').textContent = text('reconnecting'); };
    window.addEventListener('pagehide', () => chat.close(), {once: true});
}
