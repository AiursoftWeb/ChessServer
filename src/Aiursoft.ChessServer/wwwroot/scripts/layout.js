import { autoTheme } from "../node_modules/@aiursoft/autodark.js/dist/esm/autodark.js";
import { getUserName, changeName } from "./player.js";
autoTheme();

async function loadName() {
  document.getElementById("player-nick-name").innerText = await getUserName();
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
