const root = document.getElementById('managed-room');
const id = root.dataset.roomId;
const embeddedGame = root.dataset.gameId || null;
const el = name => document.getElementById(name);
const text = key => document.querySelector(`#room-loc [data-key="${key}"]`)?.textContent ?? textFallback;
const textFallback = 'Could not update the room. Please try again.';
let state, busy = false, closed = false, renderedVersion = -1, dirty = false, editingVersion, aiVersion, socket, chat;
let drag;
const settings = el('room-settings');
let activePanel = null, unread = 0;
function selectPanel(name, focus = false) {
    activePanel = name;
    for (const tab of ['moves', 'chat']) {
        const selected = tab === name;
        el(`room-${tab}-tab`).setAttribute('aria-expanded', String(selected));
        el(`room-${tab}`).hidden = !selected;
    }
    if (name === 'chat') { unread = 0; el('room-unread').hidden = true; }
    if (focus && name) el(`room-${name}-tab`).focus();
}
if (embeddedGame) {
    // Move the existing controls without replacing nodes or their event handlers.
    el('room-game-state').append(el('game-state'), el('game-result'));
    el('room-moves').prepend(el('game-moves'));
    root.querySelector('.chess-playing-area').append(el('room-replay-navigation'));
    el('room-game-controls').append(el('game-controls'));
    el('room-primary-actions').prepend(el('history-link'), el('play-another-game'));
    el('history-link').classList.replace('btn-primary', 'btn-outline-secondary');
    el('room-game-tools').append(el('game-exports'), el('game-policy'));
    el('game-room').querySelector('.chess-information').hidden = true;
    for (const name of ['moves', 'chat']) {
        const tab = el(`room-${name}-tab`);
        tab.onclick = () => selectPanel(activePanel === name ? null : name);
        tab.onkeydown = event => {
            if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
            event.preventDefault();
            const target = event.key === 'Home' ? 'moves' : event.key === 'End' ? 'chat' : name === 'moves' ? 'chat' : 'moves';
            selectPanel(el(`room-${target}-tab`).hidden ? 'moves' : target, true);
        };
    }
} else {
    el('room-preparing-chat').append(el('room-chat'));
}

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
        return true;
    } catch { error('error'); }
    finally { busy = false; }
}
function button(key, handler, danger = false) {
    const b = document.createElement('button'); b.type = 'button'; b.className = `btn btn-sm btn-outline-${danger ? 'danger' : 'secondary'}`;
    b.textContent = text(key); b.onclick = handler; return b;
}
function avatar(url) {
    const img = document.createElement('img'); img.className = 'room-avatar';
    img.src = url; img.alt = ''; img.width = 56; img.height = 56; img.draggable = false;
    img.onerror = () => { img.onerror = null; img.src = '/images/avatars/anonymous.webp'; };
    return img;
}
function seatLabel(side) {
    return text(state.RandomColors && state.Stage === 'waiting' && side !== 'observer' ? side === 'white' ? 'player-one' : 'player-two' : side);
}
function boardPlayers() {
    if (!state.CurrentGameId) return;
    const blackView = state.GameBlack?.Id === state.Viewer;
    for (const [id, side] of [['clock-top', blackView ? 'white' : 'black'], ['clock-bottom', blackView ? 'black' : 'white']]) {
        const clock = el(id), seat = side === 'white' ? state.GameWhite : state.GameBlack;
        if (!clock || !seat) continue;
        let image = clock.querySelector('.room-avatar');
        if (!image) {
            image = avatar(seat.AvatarUrl); clock.prepend(image);
            const identity = document.createElement('div'); identity.className = 'room-board-identity';
            clock.insertBefore(identity, clock.querySelector('.clock-name')); identity.append(clock.querySelector('.clock-name'));
            const label = document.createElement('small'); label.className = 'room-board-side'; identity.append(label);
        }
        if (image.dataset.playerId !== seat.Id) { image.dataset.playerId = seat.Id; image.src = seat.AvatarUrl; }
        clock.classList.toggle('room-host', seat.Id === state.Host);
        clock.querySelector('.room-board-side').textContent = text(side) + (seat.Id === state.Host ? ` · ${text('host')}` : '');
    }
}
function person(member, seat, host) {
    const row = document.createElement('div'); row.className = 'room-person'; row.dataset.personId = member.Id;
    row.append(avatar(member.AvatarUrl));
    const identity = document.createElement('div'); identity.className = 'room-person-identity';
    const name = document.createElement('strong'); name.className = 'room-person-name';
    name.textContent = member.NickName; identity.append(name);
    if (member.Id === state.Host) {
        row.classList.add('room-host');
        const badge = document.createElement('span'); badge.className = 'badge bg-warning text-dark room-host-badge';
        badge.textContent = `★ ${text('host')}`; identity.append(badge);
    }
    row.append(identity);
    if (!host) return row;
    const grip = document.createElement('span'); grip.className = 'room-drag-grip';
    grip.title = text('drag'); grip.setAttribute('aria-hidden', 'true'); row.prepend(grip);
    row.draggable = true;
    row.onmousedown = event => { row.draggable = !event.target.closest('button, select, option'); };
    row.ondragstart = event => {
        if (busy || closed || state.Host !== state.Viewer || event.target !== row) { event.preventDefault(); return; }
        clearDrag();
        const preview = document.createElement('div'); preview.className = 'room-person room-drag-preview';
        preview.append(avatar(member.AvatarUrl), identity.cloneNode(true)); document.body.append(preview);
        drag = {id: member.Id, seat, version: state.Version, row, preview};
        event.dataTransfer.setData('text/plain', member.Id); event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setDragImage(preview, 32, 32);
        root.classList.add('room-dragging');
        document.querySelectorAll('[data-drop-seat]').forEach(target => {
            target.classList.toggle('room-drop-ready', canDrop(target));
            target.classList.toggle('room-drop-blocked', !canDrop(target) && target.dataset.dropSeat !== seat);
        });
        requestAnimationFrame(() => { if (drag?.row === row) row.classList.add('room-person-dragging'); });
    };
    const select = document.createElement('select'); select.className = 'form-select form-select-sm'; select.setAttribute('aria-label', member.NickName);
    for (const target of ['white', 'black', 'observer']) { const o = document.createElement('option'); o.value = target; o.textContent = seatLabel(target); select.append(o); }
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
        const row = document.createElement('div'); row.className = 'room-person room-ai';
        row.append(avatar(`/images/avatars/${seat.Kind === 'llm' ? 'llm' : 'lynx'}.webp`));
        const label = document.createElement('strong'); label.className = 'room-person-identity'; label.textContent = seat.Name; row.append(label);
        if (host && state.Stage !== 'playing') row.append(button('remove-ai', () => command('remove-ai', {Seat: side}), true));
        container.append(row);
    }
}
function apply(next) {
    if (closed) return;
    if (state && next.Version < state.Version) return;
    state = next;
    if (String(state.CurrentGameId || '') !== String(embeddedGame || '')) { location.reload(); return; }
    const host = state.Host === state.Viewer;
    el('room-chat-tab').hidden = !canChat() || !embeddedGame;
    if (embeddedGame) {
        if (!canChat() && activePanel === 'chat') selectPanel(null);
        el('room-chat').hidden = !canChat() || activePanel !== 'chat';
    } else { el('room-chat').hidden = !canChat(); el('room-preparing-chat').hidden = !canChat(); }
    el('room-chat-form').hidden = !canChat();
    if (canChat()) connectChat();
    else if (chat) { const connection = chat; chat = null; connection.close(); }
    root.classList.toggle('room-in-game', !!state.CurrentGameId);
    el('room-settings-panel').hidden = !!state.CurrentGameId;
    el('room-name').textContent = state.Name; el('room-stage').textContent = text(state.Stage);
    document.querySelectorAll('[data-room-host]').forEach(b => { b.hidden = !host; });
    el('settings-fields').disabled = !host || state.Stage === 'playing';
    el('start-room').hidden = !host || state.Stage !== 'waiting'; el('start-room').disabled = !state.CanStart || dirty;
    el('next-room-game').hidden = !host || state.Stage !== 'finished';
    if (embeddedGame) el('play-another-game').hidden = state.Stage !== 'finished';
    el('retry-room-computer').hidden = !host || !state.ComputerError || state.Stage !== 'playing';
    el('interrupt-room').hidden = !host || state.Stage !== 'playing';
    el('room-history').hidden = state.Stage !== 'finished' || state.OrganizerId !== state.Viewer || state.Members.find(m => m.Id === state.Viewer)?.IsGuest !== false || [state.GameWhite?.Id, state.GameBlack?.Id].includes(state.Viewer);
    el('room-history').href = `/History/Replay/${state.HistoryId}`;
    const randomWaiting = state.RandomColors && state.Stage === 'waiting';
    el('swap-seats').hidden = !host || randomWaiting; el('swap-seats').disabled = state.Stage === 'playing';
    el('white-seat-label').textContent = seatLabel('white'); el('black-seat-label').textContent = seatLabel('black');
    el('room-color-note').hidden = !randomWaiting;
    el('show-room-people').hidden = !state.CurrentGameId;
    if (state.CurrentGameId && el('room-roster').parentElement !== el('room-people')) {
        el('room-people').append(el('room-roster')); el('room-roster').hidden = false;
    }
    boardPlayers();
    if (renderedVersion === state.Version) return;
    clearDrag();
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
            closed = true; clearDrag(); error('room-unavailable'); el('settings-fields').disabled = true;
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
el('share-room').onclick = () => el('room-share').showModal();
el('close-room-share').onclick = () => el('room-share').close();
el('show-room-people').onclick = () => el('room-people').showModal();
el('close-room-people').onclick = () => el('room-people').close();
el('copy-room-link').onclick = async () => { try { await navigator.clipboard.writeText(el('room-link').value); el('copy-room-link').textContent = text('copied'); } catch { el('room-link').select(); } };
el('ai-kind').onchange = () => { const llm = el('ai-kind').value === 'llm'; el('ai-model-field').hidden = !llm; el('ai-difficulty-field').hidden = llm; };
el('cancel-room-ai').onclick = () => el('add-room-ai').close();
el('add-ai-form').onsubmit = event => { event.preventDefault(); el('add-room-ai').close(); command('add-ai', {Seat: el('ai-seat').value, Ai: el('ai-kind').value, Difficulty: el('ai-difficulty').value, Model: el('ai-model').value}, aiVersion); };
function clearDrag() {
    drag?.row.classList.remove('room-person-dragging'); drag?.preview.remove(); drag = null;
    root.classList.remove('room-dragging');
    document.querySelectorAll('[data-drop-seat]').forEach(target => target.classList.remove('room-drop-ready', 'room-drop-over', 'room-drop-blocked'));
}
function canDrop(target) {
    if (!drag || busy || closed || state.Host !== state.Viewer || state.Version !== drag.version || target.dataset.dropSeat === drag.seat) return false;
    const seat = target.dataset.dropSeat === 'white' ? state.White : target.dataset.dropSeat === 'black' ? state.Black : null;
    return seat?.Kind !== 'human';
}
document.addEventListener('dragend', clearDrag);
document.querySelectorAll('[data-drop-seat]').forEach(target => {
    target.dataset.dropLabel = text('drop');
    target.ondragover = event => {
        if (!canDrop(target)) return;
        event.preventDefault(); event.dataTransfer.dropEffect = 'move';
        document.querySelectorAll('.room-drop-over').forEach(other => { if (other !== target) other.classList.remove('room-drop-over'); });
        target.classList.add('room-drop-over');
    };
    target.ondragleave = event => { if (!target.contains(event.relatedTarget)) target.classList.remove('room-drop-over'); };
    target.ondrop = async event => {
        event.preventDefault();
        if (!canDrop(target) || event.dataTransfer.getData('text/plain') !== drag.id) { clearDrag(); return; }
        const {id: memberId, version} = drag; clearDrag();
        if (await command('move', {Target: memberId, Seat: target.dataset.dropSeat}, version)) {
            const row = [...target.querySelectorAll('[data-person-id]')].find(person => person.dataset.personId === memberId);
            row?.classList.add('room-person-arrived');
            row?.addEventListener('animationend', () => row.classList.remove('room-person-arrived'), {once: true});
        }
    };
});
function connect() {
    if (closed) return;
    socket = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/rooms/${id}.ws`);
    socket.onmessage = () => refresh();
    socket.onclose = () => { if (!closed) setTimeout(connect, 2000); };
}
function canChat() {
    return state && ((state.White?.Kind === 'human' && state.White.Id === state.Viewer) || (state.Black?.Kind === 'human' && state.Black.Id === state.Viewer));
}
function connectChat() {
    if (closed || chat || !canChat()) return;
    const connection = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/rooms/${id}/chat.ws`);
    chat = connection;
    connection.onmessage = event => {
        if (!canChat()) return;
        const message = JSON.parse(event.data), row = document.createElement('div'); row.className = 'chess-message';
        const sender = document.createElement('strong'); sender.textContent = message.SenderNickName;
        const body = document.createElement('p'); body.textContent = message.Content; row.append(sender, body); el('room-messages').append(row);
        if (embeddedGame && activePanel !== 'chat') {
            unread++; el('room-unread').textContent = String(unread); el('room-unread').hidden = false;
        }
        el('room-messages').scrollTop = el('room-messages').scrollHeight;
        while (el('room-messages').children.length > 100) el('room-messages').firstElementChild.remove();
    };
    connection.onclose = () => { if (chat !== connection) return; chat = null; if (!closed) setTimeout(connectChat, 2000); };
}
el('room-chat-form').onsubmit = event => {
    event.preventDefault(); const input = el('room-chat-input');
    if (chat?.readyState === WebSocket.OPEN && input.value.trim()) { chat.send(input.value); input.value = ''; }
};
await refresh(); connect(); connectChat();
window.addEventListener('chess-game-ended', refresh);
const timer = setInterval(refresh, 5000);
window.addEventListener('pagehide', () => { closed = true; clearInterval(timer); socket?.close(); chat?.close(); }, {once: true});
