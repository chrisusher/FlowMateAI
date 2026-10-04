export function read(key) {
  return localStorage.getItem(key);
}

export function write(key, value) {
  localStorage.setItem(key, value);
}

export function getClientId() {
  let id = localStorage.getItem("flowmate.device.id");

  if (!id) {
    id = crypto.randomUUID();
    localStorage.setItem("flowmate.device.id", id);
  }
  return id;
}
const TimerTone = Object.freeze({
  Focus: "focus",
  Break: "break"
});

export function playTone(kind) {
  try {
    const audio = new AudioContext();
    const oscillator = audio.createOscillator();
    const gain = audio.createGain();
    oscillator.type = "sine";
    oscillator.frequency.value = kind === TimerTone.Break ? 660 : 880;
    gain.gain.setValueAtTime(0.0001, audio.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.12, audio.currentTime + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, audio.currentTime + 0.35);
    oscillator.connect(gain); gain.connect(audio.destination);
    oscillator.start(); oscillator.stop(audio.currentTime + 0.36);
    oscillator.onended = () => audio.close();
  } catch { /* Browser audio is best effort. */ }
}
