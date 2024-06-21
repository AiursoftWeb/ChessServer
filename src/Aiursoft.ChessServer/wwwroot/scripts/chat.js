const initChat = function (playerId, gameId) {
    const messagesBox = document.getElementById('messages-box');
    const inputMessage = document.getElementById('inputMessage');
    const chatSendForm = document.getElementById('chatSendForm');
    const wsScheme = window.location.protocol === "https:" ? "wss://" : "ws://";
    const socket = new WebSocket(
        `${wsScheme}${window.location.host}/chats/${gameId}.ws?playerId=${playerId}`
    );
    
    chatSendForm.onsubmit = function sendNewMessage(e) {
        e.preventDefault();
        let msg = inputMessage.value;

        if (msg.trim() === '') {
            return;
        }

        socket.send(msg);
        inputMessage.value = '';
    }

    function scrollToNewestMessage() {
        let lastChild = messagesBox.lastElementChild;
        if (lastChild) {
            lastChild.scrollIntoView({ behavior: "smooth" });
        }
    }

    function appendMyNewMessage(message) {
        let t = document.getElementById('messageFromMe').content;
        let txt = t.querySelector("[data-message]");
        txt.textContent = message;
        let clone = document.importNode(t, true);
        messagesBox.appendChild(clone);
        scrollToNewestMessage();
    }

    function appendOpponentNewMessage(message) {
        let t = document.getElementById('messageFromOpponent').content;
        let txt = t.querySelector("[data-message]");
        txt.textContent = message;
        let clone = document.importNode(t, true);
        messagesBox.appendChild(clone);
        scrollToNewestMessage();
    }

    
    socket.onmessage = function (event) {
        // event.data may be:
        // {"Content":"zxczc","SenderNickName":"Anonymous 1431","IsMe":true}
        
        const serverMessage = JSON.parse(event.data);
        if (serverMessage.IsMe) {
            appendMyNewMessage(serverMessage.Content);
        } else {
            appendOpponentNewMessage(serverMessage.Content);
        }
    };

    socket.onclose = function () {
        setTimeout(function () {
            initChat(playerId, gameId);
        }, 1000);
    };
}

export default initChat;
