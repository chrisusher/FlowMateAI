export function read(key) {
  return localStorage.getItem(key);
}

export function write(key, value) {
  localStorage.setItem(key, value);
}

export function getClientId() {
  let id = sessionStorage.getItem("flowmate.tab.id");

  if (!id) {
    id = crypto.randomUUID();
    sessionStorage.setItem("flowmate.tab.id", id);
  }
  return id;
}
const TimerTone = Object.freeze({
  Focus: "focus",
  Break: "break"
});

let timerAudioContext;

export async function playTone(kind) {
  try {
    timerAudioContext ??= new AudioContext();

    if (timerAudioContext.state === "suspended") {
      await timerAudioContext.resume();
    }

    const oscillator = timerAudioContext.createOscillator();
    const gain = timerAudioContext.createGain();
    oscillator.type = "sine";
    oscillator.frequency.value = kind === TimerTone.Break ? 660 : 880;
    gain.gain.setValueAtTime(0.0001, timerAudioContext.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.12, timerAudioContext.currentTime + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, timerAudioContext.currentTime + 0.35);
    oscillator.connect(gain);
    gain.connect(timerAudioContext.destination);
    oscillator.start();
    oscillator.stop(timerAudioContext.currentTime + 0.36);
    return true;
  } catch {
    // Autoplay policies can reject audio outside a recent user gesture.
    return false;
  }
}
