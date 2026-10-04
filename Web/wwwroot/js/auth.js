import Keycloak from "./vendor/keycloak.js";

const StorageKeys = Object.freeze({
    returnPath: "flowmate.oidc.return-path",
    legacySession: "flowmate.auth.session",
    legacyState: "flowmate.auth.state",
    legacyVerifier: "flowmate.auth.verifier"
});
let adapter;
let initialization;
let refresh;
let subscriber;
let configured = false;

function clearLegacySession() {
    for (const key of [StorageKeys.legacySession, StorageKeys.legacyState, StorageKeys.legacyVerifier]) {
        sessionStorage.removeItem(key);
    }
}

export function safeReturnPath(value) {
    try {
        const base = new URL(document.baseURI);
        const target = new URL(value || base.href, base);
        return target.origin === base.origin && target.pathname.startsWith(base.pathname)
            ? target.pathname + target.search + target.hash : base.pathname;
    } catch {
        return new URL(document.baseURI).pathname;
    }
}

function rememberPath() {
    sessionStorage.setItem(StorageKeys.returnPath, safeReturnPath(location.href));
}

function restorePath() {
    const path = sessionStorage.getItem(StorageKeys.returnPath);
    sessionStorage.removeItem(StorageKeys.returnPath);
    if (path) history.replaceState(history.state, document.title, safeReturnPath(path));
}

export function getSession() {
    const claims = adapter?.tokenParsed;
    return {
        configured,
        signedIn: !!adapter?.authenticated && !!claims?.sub,
        sub: claims?.sub,
        name: claims?.name || claims?.preferred_username || claims?.email,
        email: claims?.email
    };
}

function notify() {
    if (subscriber) void subscriber.invokeMethodAsync("SessionChanged", getSession());
}

export function subscribe(reference) { subscriber = reference; }

export function initialize(config) {
    return initialization ??= initializeCore(config);
}

async function initializeCore(config) {
    clearLegacySession();
    configured = !!(config?.url && config?.realm && config?.clientId);
    if (!configured) return getSession();
    adapter = new Keycloak(config);
    adapter.onAuthLogout = notify;
    adapter.onAuthRefreshSuccess = notify;
    adapter.onTokenExpired = () => { void getAccessToken(); };
    // Retain a deep link across the top-level SSO check on reload. Credentials and
    // tokens remain exclusively in Keycloak and the adapter's memory.
    if (!sessionStorage.getItem(StorageKeys.returnPath)) rememberPath();
    try {
        await adapter.init({
            onLoad: "check-sso",
            checkLoginIframe: false,
            pkceMethod: "S256",
            flow: "standard",
            responseMode: "fragment",
            redirectUri: document.baseURI
        });
    } catch {
        adapter.clearToken();
    }
    restorePath();
    return getSession();
}

export async function getAccessToken() {
    if (!adapter?.authenticated) return null;
    if (!refresh) {
        refresh = (async () => {
            try {
                await adapter.updateToken(30);
                return adapter.token || null;
            } catch {
                adapter.clearToken();
                notify();
                return null;
            } finally {
                refresh = undefined;
            }
        })();
    }
    return refresh;
}

export async function login(idpHint) {
    if (!adapter) return;
    rememberPath();
    await adapter.login({ redirectUri: document.baseURI, idpHint });
}

export async function register() {
    if (!adapter) return;
    rememberPath();
    await adapter.register({ redirectUri: document.baseURI });
}

export async function logout() {
    if (!adapter) return;
    clearLegacySession();
    sessionStorage.removeItem(StorageKeys.returnPath);
    // Build the end-session URL before clearing the ID token used as logout hint.
    const url = adapter.createLogoutUrl({ redirectUri: document.baseURI });
    adapter.clearToken();
    notify();
    location.assign(url);
}
