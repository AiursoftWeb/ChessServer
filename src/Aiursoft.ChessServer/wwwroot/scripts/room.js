const room = document.getElementById('chess-room');
const id = room.dataset.roomId;
const socket = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/challenge/listen/${id}.ws`);
socket.onmessage = event => { if (event.data === 'game-started') location.assign(`/games/${id}.html`); };
socket.onclose = () => { document.getElementById('connection-status').textContent = room.dataset.disconnected || 'Connection closed. Refresh to reconnect.'; };
window.addEventListener('pagehide', () => socket.close(), { once: true });
