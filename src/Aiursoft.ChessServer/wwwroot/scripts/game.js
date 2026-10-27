import ChessBuilder from './chessboard/chessboard.js';
const id = document.getElementById('game-room').dataset.gameId;
const [colorResponse, infoResponse] = await Promise.all([fetch(`/games/${id}.color`), fetch(`/games/${id}.json`)]);
if (colorResponse.ok && infoResponse.ok) {
    const info = await infoResponse.json();
    document.getElementById('versus-info').textContent = `${info.creator} vs ${info.accepter}`;
    const board = new ChessBuilder(await colorResponse.text(), document.getElementById('status'), document.getElementById('role'), document.getElementById('pieceMoveSound')).default();
    board.run(id);
    window.addEventListener('pagehide', () => board.dispose(), { once: true });
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
