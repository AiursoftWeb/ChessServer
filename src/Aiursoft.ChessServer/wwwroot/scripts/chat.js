const initChat = function (playerId, gameId) {
  const messagesBox = document.getElementById("messages-box");
  const inputMessage = document.getElementById("inputMessage");
  const chatSendForm = document.getElementById("chatSendForm");
  const wsScheme = window.location.protocol === "https:" ? "wss://" : "ws://";
  const socket = new WebSocket(
    `${wsScheme}${window.location.host}/chats/${gameId}.ws?playerId=${playerId}`
  );

  chatSendForm.onsubmit = function sendNewMessage(e) {
    e.preventDefault();
    let msg = inputMessage.value;

    if (msg.trim() === "") {
      return;
    }

    socket.send(msg);
    inputMessage.value = "";
  };

  function scrollToNewestMessage() {
    messagesBox.scrollTo({ top: messagesBox.scrollHeight, behavior: "smooth" });
  }

  function appendMyNewMessage(message, senderNickname) {
    let template = document.getElementById("messageFromMe");

    appendNewMessage(message, senderNickname, template);

    scrollToNewestMessage();
  }

  function appendOpponentNewMessage(message, senderNickname) {
    let template = document.getElementById("messageFromOpponent");

    appendNewMessage(message, senderNickname, template);

    scrollToNewestMessage();
  }

  function appendNewMessage(message, senderNickname, template) {
    let t = template.content;
    let txt = t.querySelector("[data-message]");
    txt.textContent = message;
    let nickname = t.querySelector("[data-message-from]");
    nickname.setAttribute("data-message-from", senderNickname);

    const lastSender =
      messagesBox.lastElementChild && messagesBox.lastElementChild.querySelector
        ? messagesBox.lastElementChild
            .querySelector("[data-message-from]")
            .getAttribute("data-message-from")
        : "";

    nickname.textContent = lastSender === senderNickname ? "" : senderNickname;

    let clone = document.importNode(t, true);
    messagesBox.appendChild(clone);
  }

  socket.onmessage = function (event) {
    // event.data may be:
    // {"Content":"zxczc","SenderNickName":"Anonymous 1431","IsMe":true}

    const serverMessage = JSON.parse(event.data);
    if (serverMessage.IsMe) {
      appendMyNewMessage(serverMessage.Content, serverMessage.SenderNickName);
    } else {
      appendOpponentNewMessage(
        serverMessage.Content,
        serverMessage.SenderNickName
      );
    }
  };

  socket.onclose = function () {
    setTimeout(function () {
      initChat(playerId, gameId);
    }, 1000);
  };
};

export default initChat;
