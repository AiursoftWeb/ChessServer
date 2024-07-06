const MINIMAL_BUT_NOT_MUTE = 0.0001;

function initSoundForPC() {
  if (this.soundControl === null) {
    return;
  }

  this.soundControl.volume = MINIMAL_BUT_NOT_MUTE;

  const activeSound = () => {
    if (this.soundControl.played.length === 0) {
      this.soundControl.play();
    } else {
      document.body.removeEventListener("click", activeSound);
    }
  };

  document.body.addEventListener("click", activeSound);
}

function initSoundForMobile() {
  if (this.soundControl === null) {
    return;
  }

  this.soundControl.muted = true;

  const activeSound = () => {
    if (this.soundControl.played.length === 0) {
      this.soundControl.play();
    } else {
      document.body.removeEventListener("click", activeSound);
    }
  };

  document.body.addEventListener("click", activeSound);
}

function playSoundForPC() {
  const START = 0;
  // 'this' should be points anduinChessBoard
  if (this.soundControl !== null) {
    this.soundControl.currentTime = START;
    this.soundControl.volume = 1;
    setTimeout(() => {
      this.soundControl.volume = MINIMAL_BUT_NOT_MUTE;
    }, 500);
  }
}

function playSoundForMobile() {
  // 'this' should be points anduinChessBoard
  if (this.soundControl !== null) {
    this.soundControl.currentTime = 0;
    this.soundControl.muted = false;
    setTimeout(() => {
      this.soundControl.muted = true;
    }, 500);
  }
}

export {
  initSoundForPC,
  initSoundForMobile,
  playSoundForPC,
  playSoundForMobile,
};
