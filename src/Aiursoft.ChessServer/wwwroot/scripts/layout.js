import {autoTheme} from "../node_modules/@aiursoft/autodark.js/dist/esm/autodark.js";
import {getUserName, changeName, getUserId} from "./player.js";

autoTheme();

async function loadName() {
    document.getElementById("player-nick-name").innerHTML =
        "<i class=\"fa-solid fa-user-pen mr-2\"></i>" +
        await getUserName();
    for (let element of document.getElementsByClassName("btn-difficulty")) {
        element.href += `&playerId=${await getUserId()}`;
    }
}

async function promptChangeName() {
    const newName = prompt("Please enter your new name:");
    if (newName) {
        await changeName(newName);
        await loadName();
    }
}

await loadName();
document
    .getElementById("player-nick-name")
    .addEventListener("click", promptChangeName);
