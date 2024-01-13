const guid = function () {
    return crypto.randomUUID();
};

const getUserId = function () {
    let userId = localStorage.getItem("userId");
    if (!userId) {
        userId = guid();
        localStorage.setItem("userId", userId);
    }
    return userId;
};

const getUserName = async function () {
    // call /players/{id} API:
    const response = await fetch(`/players/${getUserId()}`);
    return (await response.json()).nickName;
}

const changeName = async function (newName) {
    // call /players/{id}/{new-name} HTTP PUT API:
    await fetch(`/players/${getUserId()}/new-name/${newName}`, { method: 'PUT' });
 }

export { getUserId, getUserName, changeName };