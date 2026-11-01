const root = document.getElementById('managed-room');
const id = root.dataset.roomId;
const embeddedGame = root.dataset.gameId || null;
const el = name => document.getElementById(name);
const text = key => document.querySelector(`#room-loc [data-key="${key}"]`)?.textContent ?? textFallback;
const textFallback = 'Could not update the room. Please try again.';
let state, busy = false, closed = false, renderedVersion = -1, dirty = false, editingVersion, aiVersion, socket, chat;
const settings = el('room-settings');
function error(key) { el('room-error').textContent = text(key); el('room-error').hidden = false; }
async function command(kind, fields = {}, version = state?.Version) {
    if (busy || closed || !state) return;
    busy = true; el('room-error').hidden = true;
    try {
        const payload = new URLSearchParams({Kind: kind, Version: version, CommandId: crypto.randomUUID(), ...fields});
        const response = await fetch(`/rooms/${id}/action`, {method: 'POST', body: payload, headers: {RequestVerificationToken: el('room-antiforgery').querySelector('input').value}});
        const result = await response.json();
        if (!response.ok) {
            if (result.Error === 'room-confirm-interruption' || result.Error === 'room-confirm-replace-ai') {
                const accepted = confirm(text(result.Error === 'room-confirm-interruption' ? 'confirm-interrupt' : 'confirm-replace'));
                busy = false;
                if (accepted) return command(kind, {...fields, Confirm: 'true'}, version);
            } else error(result.Error || 'error');
            if (result.Error === 'room-stale') dirty = false;
            await refresh(); return;
        }
        if (result.Left) { closed = true; location.assign('/Lobby'); return; }
        dirty = false; apply(result);
    } catch { error('error'); }
    finally { busy = false; }
}
function button(key, handler, danger = false) {
    const b = document.createElement('button'); b.type = 'button'; b.className = `btn btn-sm btn-outline-${danger ? 'danger' : 'secondary'}`;
    b.textContent = text(key); b.onclick = handler; return b;
}
function person(member, seat, host) {
    const row = document.createElement('div'); row.className = 'room-person'; row.dataset.personId = member.Id;
    const name = document.createElement('strong'); name.className = 'room-person-name';
    name.textContent = member.NickName + (member.Id === state.Host ? ` · ${text('host')}` : ''); row.append(name);
    if (!host) return row;
    row.draggable = true;
    row.ondragstart = event => { event.dataTransfer.setData('text/plain', member.Id); event.dataTransfer.effectAllowed = 'move'; };
    const select = document.createElement('select'); select.className = 'form-select form-select-sm'; select.setAttribute('aria-label', member.NickName);
    for (const target of ['white', 'black', 'observer']) { const o = document.createElement('option'); o.value = target; o.textContent = text(target); select.append(o); }
    select.value = seat; select.onchange = () => command('move', {Target: member.Id, Seat: select.value}); row.append(select);
    if (member.Id !== state.Host) row.append(button('transfer', () => command('transfer', {Target: member.Id})));
    if (member.Id !== state.Viewer) row.append(button('kick', () => { if (confirm(text('confirm-kick'))) command('kick', {Target: member.Id}); }, true));
    return row;
}
function drawSeat(side, seat, host) {
    const container = el(`${side}-seat`); container.replaceChildren();
    if (!seat) {
        const label = document.createElement('p'); label.textContent = text('empty'); label.className = 'text-muted'; container.append(label);
        if (host && state.Stage !== 'playing') container.append(button('add-ai', () => { aiVersion = state.Version; el('ai-seat').value = side; el('add-room-ai').showModal(); }));
    } else if (seat.Kind === 'human') {
        const member = state.Members.find(m => m.Id === seat.Id);
        if (member) container.append(person(member, side, host));
    } else {
        const label = document.createElement('strong'); label.textContent = seat.Name; container.append(label);
        if (host && state.Stage !== 'playing') container.append(button('remove-ai', () => command('remove-ai', {Seat: side}), true));
    }
}
function apply(next) {
    if (closed) return;
    if (state && next.Version < state.Version) return;
    state = next;
    if (String(state.CurrentGameId || '') !== String(embeddedGame || '')) { location.reload(); return; }
    const host = state.Host === state.Viewer;
    el('room-chat-form').hidden = !((state.White?.Kind === 'human' && state.White.Id === state.Viewer) || (state.Black?.Kind === 'human' && state.Black.Id === state.Viewer));
    el('room-name').textContent = state.Name; el('room-stage').textContent = text(state.Stage);
    document.querySelectorAll('[data-room-host]').forEach(b => { b.hidden = !host; });
    el('settings-fields').disabled = !host || state.Stage === 'playing';
    el('start-room').hidden = !host || state.Stage !== 'waiting'; el('start-room').disabled = !state.CanStart || dirty;
    el('next-room-game').hidden = !host || state.Stage !== 'finished';
    el('retry-room-computer').hidden = !host || !state.ComputerError || state.Stage !== 'playing';
    el('interrupt-room').hidden = !host || state.Stage !== 'playing';
    el('room-history').hidden = state.Stage !== 'finished' || state.OrganizerId !== state.Viewer || state.Members.find(m => m.Id === state.Viewer)?.IsGuest !== false;
    el('room-history').href = `/History/Replay/${state.HistoryId}`;
    el('swap-seats').disabled = state.Stage === 'playing';
    if (renderedVersion === state.Version) return;
    renderedVersion = state.Version;
    drawSeat('white', state.White, host); drawSeat('black', state.Black, host);
    el('room-observers').replaceChildren(...state.Members.filter(m => m.Id !== state.White?.Id && m.Id !== state.Black?.Id).map(m => person(m, 'observer', host)));
    if (!dirty) {
        el('room-title').value = state.Name; el('room-minutes').value = state.Minutes; el('room-increment').value = state.IncrementSeconds;
        el('room-public').checked = state.Public; el('room-random').checked = state.RandomColors; editingVersion = state.Version;
    }
}
async function refresh() {
    if (closed) return;
    try {
        const response = await fetch(`/rooms/${id}.json`);
        if (!response.ok) {
            closed = true; error('room-unavailable'); el('settings-fields').disabled = true;
            document.querySelectorAll('#managed-room button').forEach(b => { b.disabled = true; });
            if (el('game-room')) el('game-room').inert = true;
            socket?.close(); chat?.close(); return;
        }
        apply(await response.json());
    } catch { error('error'); }
}
settings.addEventListener('input', () => { if (!dirty) editingVersion = state.Version; dirty = true; el('start-room').disabled = true; });
settings.onsubmit = event => {
    event.preventDefault(); const values = Object.fromEntries(new FormData(settings));
    values.Public = el('room-public').checked; values.RandomColors = el('room-random').checked;
    command('settings', values, editingVersion);
};
el('room-minutes').onchange = () => { if (el('room-minutes').value === '0') el('room-increment').value = '0'; };
el('start-room').onclick = () => command('start');
el('swap-seats').onclick = () => command('swap');
el('next-room-game').onclick = () => command('next');
el('retry-room-computer').onclick = () => command('retry');
el('interrupt-room').onclick = () => { if (confirm(text('confirm-interrupt'))) command('interrupt', {Confirm: 'true'}); };
el('leave-room').onclick = () => { if (confirm(text('confirm-leave'))) command('leave'); };
el('copy-room-link').onclick = async () => { try { await navigator.clipboard.writeText(el('room-link').value); el('copy-room-link').textContent = text('copied'); } catch { el('room-link').select(); } };
el('ai-kind').onchange = () => { const llm = el('ai-kind').value === 'llm'; el('ai-model-field').hidden = !llm; el('ai-difficulty-field').hidden = llm; };
el('cancel-room-ai').onclick = () => el('add-room-ai').close();
el('add-ai-form').onsubmit = event => { event.preventDefault(); el('add-room-ai').close(); command('add-ai', {Seat: el('ai-seat').value, Ai: el('ai-kind').value, Difficulty: el('ai-difficulty').value, Model: el('ai-model').value}, aiVersion); };
document.querySelectorAll('[data-drop-seat]').forEach(target => {
    target.ondragover = event => { if (state?.Host === state?.Viewer) event.preventDefault(); };
    target.ondrop = event => { event.preventDefault(); if (state?.Host === state?.Viewer) command('move', {Target: event.dataTransfer.getData('text/plain'), Seat: target.dataset.dropSeat}); };
});
function connect() {
    if (closed) return;
    socket = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/rooms/${id}.ws`);
    socket.onmessage = () => refresh();
    socket.onclose = () => { if (!closed) setTimeout(connect, 2000); };
}
function connectChat() {
    if (closed) return;
    chat = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/rooms/${id}/chat.ws`);
    chat.onmessage = event => {
        const message = JSON.parse(event.data), row = document.createElement('div'); row.className = 'chess-message';
        const sender = document.createElement('strong'); sender.textContent = message.SenderNickName;
        const body = document.createElement('p'); body.textContent = message.Content; row.append(sender, body); el('room-messages').append(row);
        while (el('room-messages').children.length > 100) el('room-messages').firstElementChild.remove();
    };
    chat.onclose = () => { if (!closed) setTimeout(connectChat, 2000); };
}
el('room-chat-form').onsubmit = event => {
    event.preventDefault(); const input = el('room-chat-input');
    if (chat?.readyState === WebSocket.OPEN && input.value.trim()) { chat.send(input.value); input.value = ''; }
};
await refresh(); connect(); connectChat();
window.addEventListener('chess-game-ended', refresh);
const timer = setInterval(refresh, 5000);
window.addEventListener('pagehide', () => { closed = true; clearInterval(timer); socket?.close(); chat?.close(); }, {once: true});
