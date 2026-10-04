#!/usr/bin/env python3
"""Idempotently reconcile FlowMate realm settings without replacing user data."""

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request


def required(name):
    value = os.environ.get(name, "").strip()
    if not value:
        raise RuntimeError(f"Set {name} before provisioning.")
    return value


base = required("KEYCLOAK_URL").rstrip("/")
realm = os.environ.get("KEYCLOAK_REALM", "flowmate").strip()
admin_user = required("KEYCLOAK_ADMIN_USERNAME")
admin_password = required("KEYCLOAK_ADMIN_PASSWORD")


def request(method, path, token=None, payload=None, form=None, allow_missing=False):
    headers = {"Accept": "application/json"}
    data = None
    if payload is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(payload).encode()
    elif form is not None:
        headers["Content-Type"] = "application/x-www-form-urlencoded"
        data = urllib.parse.urlencode(form).encode()
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            body = response.read()
            return response.status, json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        body = error.read().decode(errors="replace")
        if allow_missing and error.code == 404:
            return 404, None
        raise RuntimeError(f"Keycloak {method} {path} failed ({error.code}): {body[:500]}") from error


_, token_result = request("POST", "/realms/master/protocol/openid-connect/token", form={
    "grant_type": "password", "client_id": "admin-cli",
    "username": admin_user, "password": admin_password,
})
token = token_result["access_token"]
root = "/admin/realms"
quoted_realm = urllib.parse.quote(realm, safe="")
status, current = request("GET", f"{root}/{quoted_realm}", token, allow_missing=True)
realm_settings = {
    "realm": realm,
    "enabled": True,
    "sslRequired": "external",
    "registrationAllowed": True,
    "registrationEmailAsUsername": True,
    "loginWithEmailAllowed": True,
    "duplicateEmailsAllowed": False,
    "verifyEmail": True,
    "resetPasswordAllowed": True,
    "bruteForceProtected": True,
    "failureFactor": 5,
    "waitIncrementSeconds": 60,
    "maxFailureWaitSeconds": 900,
    "passwordPolicy": "length(15) and notUsername",
    "accessTokenLifespan": 300,
    "ssoSessionIdleTimeout": 1800,
    "ssoSessionMaxLifespan": 28800,
}
if status == 404:
    request("POST", root, token, realm_settings)
else:
    # Update settings in place. Existing users, credentials and realm signing keys are preserved.
    request("PUT", f"{root}/{quoted_realm}", token, {**current, **realm_settings})


def clients(client_id):
    encoded = urllib.parse.quote(client_id, safe="")
    _, rows = request("GET", f"{root}/{quoted_realm}/clients?clientId={encoded}", token)
    return rows


def upsert_api_audience_scope():
    _, scopes = request("GET", f"{root}/{quoted_realm}/client-scopes?search=flowmate-api-audience", token)
    scope = {
        "name": "flowmate-api-audience",
        "description": "Adds the FlowMate API audience to browser access tokens",
        "protocol": "openid-connect",
        "attributes": {"include.in.token.scope": "true", "display.on.consent.screen": "false"},
        "protocolMappers": [{
            "name": "FlowMate API audience",
            "protocol": "openid-connect",
            "protocolMapper": "oidc-audience-mapper",
            "consentRequired": False,
            "config": {
                "included.client.audience": "flowmate-api",
                "id.token.claim": "false",
                "access.token.claim": "true",
            },
        }],
    }
    matching = [row for row in scopes if row.get("name") == scope["name"]]
    if matching:
        scope["id"] = matching[0]["id"]
        request("PUT", f"{root}/{quoted_realm}/client-scopes/{scope['id']}", token, scope)
        return scope["id"]
    request("POST", f"{root}/{quoted_realm}/client-scopes", token, scope)
    _, scopes = request("GET", f"{root}/{quoted_realm}/client-scopes?search=flowmate-api-audience", token)
    matching = [row for row in scopes if row.get("name") == scope["name"]]
    if not matching:
        raise RuntimeError("Keycloak did not create the FlowMate API audience scope.")
    return matching[0]["id"]


def upsert_client(client_id, representation):
    found = clients(client_id)
    if found:
        representation["id"] = found[0]["id"]
        request("PUT", f"{root}/{quoted_realm}/clients/{representation['id']}", token, representation)
        return representation["id"]
    _, _ = request("POST", f"{root}/{quoted_realm}/clients", token, representation)
    found = clients(client_id)
    if not found:
        raise RuntimeError(f"Keycloak did not create client {client_id}.")
    return found[0]["id"]


redirect_uris = [value.strip() for value in os.environ.get("FLOWMATE_FRONTEND_REDIRECT_URIS", "").split(",") if value.strip()]
web_origins = [value.strip().rstrip("/") for value in os.environ.get("FLOWMATE_FRONTEND_ORIGINS", "").split(",") if value.strip()]
if not redirect_uris or not web_origins:
    raise RuntimeError("Set exact FLOWMATE_FRONTEND_REDIRECT_URIS and FLOWMATE_FRONTEND_ORIGINS allow-lists.")

upsert_api_audience_scope()
upsert_client("flowmate-web", {
    "clientId": "flowmate-web", "name": "FlowMate browser application", "enabled": True,
    "protocol": "openid-connect", "publicClient": True, "standardFlowEnabled": True,
    "implicitFlowEnabled": False, "directAccessGrantsEnabled": False,
    "serviceAccountsEnabled": False, "redirectUris": redirect_uris, "webOrigins": web_origins,
    "defaultClientScopes": ["profile", "email", "flowmate-api-audience"],
    "attributes": {"pkce.code.challenge.method": "S256", "post.logout.redirect.uris": "+"},
})
upsert_client("flowmate-api", {
    "clientId": "flowmate-api", "name": "FlowMate API audience", "enabled": True,
    "protocol": "openid-connect", "bearerOnly": True, "publicClient": False,
    "standardFlowEnabled": False, "implicitFlowEnabled": False, "directAccessGrantsEnabled": False,
})
cli_client = upsert_client("flowmate-cli", {
    "clientId": "flowmate-cli", "name": "FlowMate administrative CLI", "enabled": True,
    "protocol": "openid-connect", "publicClient": False, "standardFlowEnabled": False,
    "implicitFlowEnabled": False, "directAccessGrantsEnabled": False, "serviceAccountsEnabled": True,
    **({"secret": os.environ["KEYCLOAK_CLI_CLIENT_SECRET"]}
       if os.environ.get("KEYCLOAK_CLI_CLIENT_SECRET", "").strip() else {}),
})

_, service_account = request("GET", f"{root}/{quoted_realm}/clients/{cli_client}/service-account-user", token)
_, management_clients = request("GET", f"{root}/{quoted_realm}/clients?clientId=realm-management", token)
if not management_clients:
    raise RuntimeError("Keycloak realm-management client is missing.")
management_id = urllib.parse.quote(management_clients[0]["id"], safe="")
_, assigned_roles = request("GET", f"{root}/{quoted_realm}/users/{service_account['id']}/role-mappings/clients/{management_id}", token)
_, available_roles = request("GET", f"{root}/{quoted_realm}/users/{service_account['id']}/role-mappings/clients/{management_id}/available", token)
required_roles = {"query-users", "view-users"}
assigned_names = {role.get("name") for role in assigned_roles}
assign = [role for role in available_roles if role.get("name") in required_roles - assigned_names]
if (assigned_names | {role.get("name") for role in assign}) & required_roles != required_roles:
    raise RuntimeError("Could not find both query-users and view-users realm-management roles.")
if assign:
    request("POST", f"{root}/{quoted_realm}/users/{service_account['id']}/role-mappings/clients/{management_id}", token, assign)

providers = [
    ("google", "google", None),
    ("github", "github", None),
    ("microsoft-personal", "oidc", "https://login.microsoftonline.com/consumers/v2.0"),
    ("microsoft-work-school", "oidc", "https://login.microsoftonline.com/organizations/v2.0"),
]
for alias, provider_id, issuer in providers:
    prefix = "IDP_" + alias.upper().replace("-", "_")
    client_id = os.environ.get(prefix + "_CLIENT_ID", "").strip()
    client_secret = os.environ.get(prefix + "_CLIENT_SECRET", "").strip()
    if bool(client_id) != bool(client_secret):
        raise RuntimeError(f"Set both {prefix}_CLIENT_ID and {prefix}_CLIENT_SECRET, or neither.")
    if not client_id:
        continue
    config = {"clientId": client_id, "clientSecret": client_secret, "clientAuthMethod": "client_secret_post"}
    if issuer:
        root_issuer = issuer.removesuffix("/v2.0")
        config.update({
            "issuer": issuer,
            "authorizationUrl": root_issuer + "/oauth2/v2.0/authorize",
            "tokenUrl": root_issuer + "/oauth2/v2.0/token",
            "userInfoUrl": "https://graph.microsoft.com/oidc/userinfo",
            "defaultScope": "openid profile email",
        })
    representation = {"alias": alias, "providerId": provider_id, "enabled": True,
                      "trustEmail": False, "storeToken": False, "addReadTokenRoleOnCreate": False,
                      "config": config}
    _, existing = request("GET", f"{root}/{quoted_realm}/identity-provider/instances/{urllib.parse.quote(alias, safe='')}", token, allow_missing=True)
    if existing is None:
        request("POST", f"{root}/{quoted_realm}/identity-provider/instances", token, representation)
    else:
        request("PUT", f"{root}/{quoted_realm}/identity-provider/instances/{urllib.parse.quote(alias, safe='')}", token, {**existing, **representation})

smtp = {name: os.environ.get("SMTP_" + name.upper().replace("-", "_"), "").strip()
        for name in ("host", "port", "from", "fromDisplayName", "replyTo", "auth", "user", "password", "ssl", "starttls")}
if smtp["host"] or smtp["from"]:
    if not smtp["host"] or not smtp["from"]:
        raise RuntimeError("SMTP_HOST and SMTP_FROM are both required to configure email actions.")
    _, current = request("GET", f"{root}/{quoted_realm}", token)
    request("PUT", f"{root}/{quoted_realm}", token, {**current, "smtpServer": smtp})

print(f"Reconciled realm '{realm}' in place. Existing accounts and signing keys were preserved.")
