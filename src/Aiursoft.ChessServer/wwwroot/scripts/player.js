﻿const guid = function () {
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
    const response = await fetch(`/players/${getUserId()}/new-name/${newName}`, {method: 'PUT'});
    if (!response.ok) {
        alert(`Failed to change your name. ${await response.text()}`);
    }
}

const getAcceptedChallenge = async function (challengeId) {
    const response = await fetch(`/games/${challengeId}.json`);
    return await response.json();
}

const acceptChallenge = async function (challengeId) {
    const playerId = getUserId();
    await fetch(`/challenge/accept/${challengeId}?playerId=${playerId}`, {method: 'POST'});
}

const getPlayerColor = async function (challengeId) {
    const playerId = getUserId();
    const response = await fetch(`/games/${challengeId}.color?playerId=${playerId}`);
    return await response.text();
}

export {getUserId, getUserName, changeName, getAcceptedChallenge, acceptChallenge, getPlayerColor};