# Auth0 setup for Azure

FlowMate uses Auth0 for sign-in and as the issuer of access tokens for the Functions API. The browser app and API use the same Auth0 API audience, but their settings are configured separately:

- The GitHub Actions workflow embeds the public SPA settings into the Blazor WebAssembly files when it builds the frontend.
- The Function App reads its issuer, audience, and allowed frontend origin from application settings at runtime.

## 1. Create the Auth0 API

In the Auth0 Dashboard, open **Applications → APIs → Create API**. Choose a name such as `FlowMate API`, set an identifier (for example `https://api.flowmate.ai`), and select **RS256** as the signing algorithm. Save the identifier: it is the audience used by both the SPA and API. The identifier is a value you choose; it does not need to resolve as a website.

The API validator requires an RS256 JWT whose issuer is your Auth0 tenant and whose `aud` contains this exact identifier. See [Auth0's API configuration guidance](https://auth0.com/docs/get-started/architecture-scenarios/server-application-api/part-2).

## 2. Create the SPA application

In **Applications → Applications**, create an application with type **Single Page Application**. From its settings, record the **Domain** and **Client ID**. The client secret is not used by this browser application; do not add it to the frontend or GitHub variables.

After the Static Web App has its production hostname, add that origin to the application's URL allowlists. For a site at `https://<your-site>.azurestaticapps.net`, use:

| Auth0 application setting | Value |
| --- | --- |
| Allowed Callback URLs | `https://<your-site>.azurestaticapps.net/` |
| Allowed Logout URLs | `https://<your-site>.azurestaticapps.net/` |
| Allowed Web Origins | `https://<your-site>.azurestaticapps.net` |

The app sends the current browser URL (`origin + pathname`) as both its callback URL and logout return URL. The root URL above is sufficient for the normal root-hosted deployment. If you host the app under a path or allow sign-in to start on another path, add those exact callback and logout URLs too. Keep the origin without a path in **Allowed Web Origins**. Auth0 checks these values against the URLs sent by the app; see [Auth0 callback and logout URL guidance](https://auth0.com/docs/quickstart/spa/svelte).

## 3. Configure social connections

The sign-in screen offers four providers. Create/configure the corresponding Auth0 connections, enable each one for the SPA application in its **Connections** tab, and set up provider credentials/consent as required by each identity provider.

| Sign-in option | Connection name sent by FlowMate |
| --- | --- |
| Google | `google-oauth2` |
| GitHub | `github` |
| Microsoft personal account | `windowslive` |
| Microsoft work or school | `azuread` |

Connection names are Auth0 identifiers and must match exactly. If your Auth0 tenant uses different names, update the `Auth0:Connections` values in `Web/appsettings.json` before the workflow builds the site. This configuration is public and ships with the frontend.

## 4. Configure GitHub Actions for the frontend

In the GitHub repository, add these under **Settings → Secrets and variables → Actions → Variables** (they are public client settings, not secrets):

| Variable | Value |
| --- | --- |
| `AUTH0_DOMAIN` | The Auth0 tenant domain, e.g. `your-tenant.eu.auth0.com` (with or without `https://`) |
| `AUTH0_CLIENT_ID` | The SPA application's client ID |
| `AUTH0_AUDIENCE` | The API identifier from step 1, exactly as entered |
| `API_BASE_URL` | The Functions API HTTPS origin, e.g. `https://<your-function-app>.azurewebsites.net` |

The existing `azure-deploy.yml` workflow reads these variables while building the web app and writes them into the published `appsettings.json`. Set or change them before running the workflow; changing a GitHub variable does not update an already deployed static bundle until the workflow runs again. Also configure the deployment values described in [deployment.md](deployment.md): `AZURE_STATIC_WEB_APPS_API_TOKEN`, `AZURE_FUNCTIONAPP_NAME`, and `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`.

No Auth0 client secret belongs in GitHub Actions for this flow. The Blazor app uses OAuth authorization code with PKCE and keeps the access token in browser session storage.

## 5. Configure the Azure Function App

In the Function App's **Settings → Environment variables** (or Configuration), add these application settings:

| Setting name | Value |
| --- | --- |
| `Auth0__Authority` | Auth0 issuer URL, e.g. `https://your-tenant.eu.auth0.com/` (include `https://` and the trailing slash) |
| `Auth0__Audience` | The same API identifier used as `AUTH0_AUDIENCE` |
| `Cors__AllowedOrigins__0` | Exact Static Web App origin, e.g. `https://<your-site>.azurestaticapps.net` (no path or trailing slash) |

The API validates the issuer, audience, expiry, and RS256 signature using the Auth0 JWKS endpoint at `<Authority>.well-known/jwks.json`. It does not use an Auth0 client secret. The custom Functions middleware uses `Cors:AllowedOrigins` to answer preflight requests and return CORS headers; the origin must match the browser's site origin exactly. If you have multiple frontend origins, add indexed settings such as `Cors__AllowedOrigins__1` for each explicitly allowed origin.

Apply and save the app settings, then restart the Function App if Azure prompts you to. These Auth0 settings are separate from the other server-side settings listed in [deployment.md](deployment.md) and `API/appsettings.example.json`.

## 6. Deploy and verify

1. Deploy the Static Web App and Functions API using the repository's GitHub Actions workflow.
2. Confirm the published frontend was built with the intended Auth0 variables, and confirm the Function App has the settings above.
3. Open the Static Web App and sign in with each enabled provider. A callback mismatch usually means the browser's current URL is missing from **Allowed Callback URLs**; logout requires the return URL in **Allowed Logout URLs**.
4. Confirm the API is reachable at `https://<your-function-app>.azurewebsites.net/api/health`. Then sign in and load/save a workspace. Protected API calls must include a valid bearer token; an incorrect issuer/audience, an unavailable JWKS endpoint, or an unlisted CORS origin prevents successful browser API calls.

The Function API's health endpoint is public and does not test Auth0 token validation. Workspace and account-specific billing operations require a valid token. Auth0 Management API credentials are only needed for the separate administrative CLI workflows documented in [administrative-cli.md](administrative-cli.md).
