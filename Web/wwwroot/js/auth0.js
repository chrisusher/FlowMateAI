const sessionKey = "flowmate.auth.session";
const stateKey = "flowmate.auth.state";
const verifierKey = "flowmate.auth.verifier";

function toBase64Url(bytes) {
  let binary = "";
  bytes.forEach(byte => binary += String.fromCharCode(byte));
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}
function randomString(size = 48) {
  const bytes = crypto.getRandomValues(new Uint8Array(size));
  return toBase64Url(bytes);
}
async function challenge(verifier) {
  return toBase64Url(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier))));
}
function authority(domain) {
  return domain.startsWith("https://") ? domain.replace(/\/$/, "") : `https://${domain.replace(/\/$/, "")}`;
}
function redirectUri() { return `${location.origin}${location.pathname}`; }

export async function initialize(config) {
  if (!config?.domain || !config?.clientId || !config?.audience) return { configured: false };
  const url = new URL(location.href);
  const code = url.searchParams.get("code");
  if (code) {
    const returnedState = url.searchParams.get("state");
    const expectedState = sessionStorage.getItem(stateKey);
    const verifier = sessionStorage.getItem(verifierKey);
    sessionStorage.removeItem(stateKey); sessionStorage.removeItem(verifierKey);
    history.replaceState({}, document.title, redirectUri());
    if (!returnedState || returnedState !== expectedState || !verifier) throw new Error("The sign-in response could not be verified. Please try again.");
    const response = await fetch(`${authority(config.domain)}/oauth/token`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ grant_type: "authorization_code", client_id: config.clientId, code, redirect_uri: redirectUri(), code_verifier: verifier })
    });
    if (!response.ok) throw new Error("Sign-in could not be completed. Check the Auth0 application settings.");
    const tokens = await response.json();
    const info = await fetch(`${authority(config.domain)}/userinfo`, { headers: { Authorization: `Bearer ${tokens.access_token}` } });
    if (!info.ok) throw new Error("Your account profile could not be loaded.");
    const user = await info.json();
    const session = { sub: user.sub, name: user.name || user.nickname || user.email || "FlowMate user", email: user.email || "", accessToken: tokens.access_token, expiresAt: Date.now() + tokens.expires_in * 1000 };
    sessionStorage.setItem(sessionKey, JSON.stringify(session));
  }
  const raw = sessionStorage.getItem(sessionKey);
  if (!raw) return { configured: true, signedIn: false };
  const session = JSON.parse(raw);
  if (session.expiresAt <= Date.now()) { sessionStorage.removeItem(sessionKey); return { configured: true, signedIn: false }; }
  return { configured: true, signedIn: true, sub: session.sub, name: session.name, email: session.email, accessToken: session.accessToken };
}

export async function login(config, connection) {
  const state = randomString();
  const verifier = randomString(64);
  sessionStorage.setItem(stateKey, state);
  sessionStorage.setItem(verifierKey, verifier);
  const params = new URLSearchParams({
    response_type: "code", client_id: config.clientId, redirect_uri: redirectUri(),
    scope: "openid profile email", audience: config.audience, state,
    code_challenge: await challenge(verifier), code_challenge_method: "S256", prompt: "select_account"
  });
  if (connection) params.set("connection", connection);
  location.assign(`${authority(config.domain)}/authorize?${params.toString()}`);
}

export function logout(config) {
  sessionStorage.removeItem(sessionKey);
  const returnTo = redirectUri();
  const params = new URLSearchParams({ client_id: config.clientId, returnTo });
  location.assign(`${authority(config.domain)}/v2/logout?${params.toString()}`);
}

export function getSession() {
  const raw = sessionStorage.getItem(sessionKey);
  return raw ? JSON.parse(raw) : null;
}
