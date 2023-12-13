const guid = function () {
    // Create a new GUID:
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        const r = Math.random() * 16 | 0,
            v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
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
    const response = await fetch(`/players/${getUserId()}/new-name/${newName}`, { method: 'PUT' });
    return (await response.json()).nickName;
 }

export { getUserName, changeName };