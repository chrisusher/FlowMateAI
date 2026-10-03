const Theme = Object.freeze({
    Light: "light",
    Dark: "dark"
});

export function getSavedTheme() {
    return window.localStorage.getItem('theme');
}

export function prefersDarkTheme() {
    return window.matchMedia(`(prefers-color-scheme: ${Theme.Dark})`).matches;
}

export function applyTheme(theme) {
    document.documentElement.setAttribute('data-bs-theme', theme);
}

export function saveTheme(theme) {
    window.localStorage.setItem('theme', theme);
}

export function getAppliedTheme() {
    return document.documentElement.getAttribute('data-bs-theme');
}
