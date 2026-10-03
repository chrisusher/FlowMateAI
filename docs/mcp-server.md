# FlowMate MCP server

The read-only MCP server is available to Free and Pro accounts. It exposes Streamable HTTP at:

```text
https://<mcp-function-app>/runtime/webhooks/mcp
```

Create an API key in **Settings → MCP & API keys**. Keys are shown once. Configure the MCP client to send it in the `X-Api-Key` header. Never paste a key into a prompt, commit it, or store it in browser local storage. Replace a credential by creating a new key, updating the client, then revoking the old key.

## Client configuration

For clients using a JSON MCP server configuration:

```json
{
  "servers": {
    "flowmate": {
      "type": "http",
      "url": "https://<mcp-function-app>/runtime/webhooks/mcp",
      "headers": {
        "X-Api-Key": "${input:flowmate-api-key}"
      }
    }
  },
  "inputs": [
    {
      "type": "promptString",
      "id": "flowmate-api-key",
      "description": "FlowMate MCP API key",
      "password": true
    }
  ]
}
```

In other clients, add a remote Streamable HTTP server with the endpoint above and a custom header named `X-Api-Key`. Free and Pro accounts can both connect. Free accounts share 10 tool calls per rolling minute and 100 calls per UTC day across all keys, tools, and clients. Pro accounts bypass MCP rate limiting.

## Tools

- `list_projects` returns account project IDs/names, workspace time zone, available history, and the applicable rate policy.
- `get_timesheet` accepts `period` (`day`, `week`, `month`), optional `date` (`YYYY-MM-DD`, defaults to today in the workspace time zone), and an optional pagination `cursor`. It returns up to 100 focus entries per call. The daily, project, and overall totals cover the complete accessible period. Pass `nextCursor` to retrieve another page. A workspace change invalidates the cursor; restart without a cursor.
- `get_project_time` accepts `projectId`, `period`, and optional `date`, and returns total minutes, session count, and daily breakdown.

Dates are assigned by the local start date of each completed focus entry; weeks run Monday through Sunday. Free accounts can access today and the preceding 29 calendar days. Pro history begins one year before today. Responses flag partially available ranges; a fully inaccessible range returns `history_unavailable`. Running timers and breaks are excluded.

When Free limits are exhausted, the tool returns `rate_limited` with remaining allowances, reset timestamps, and `retryAfterSeconds`. Wait the indicated time before trying again. Temporary admission failures return `temporarily_unavailable`; retry with backoff.

## Provisioning and deployment

The MCP Function App is deployed separately from the existing API. Configure these repository values and secrets for GitHub Actions:

- Variable `AZURE_MCP_FUNCTIONAPP_NAME`: existing Linux Flex Consumption Function App name.
- Variable `MCP_ENDPOINT`: its public MCP endpoint, including `/runtime/webhooks/mcp`.
- Secret `AZURE_MCP_FUNCTIONAPP_PUBLISH_PROFILE`: that app's publish profile.

Configure the Function App with the Cosmos endpoint/database name and host storage settings. The MCP app uses managed identity for Cosmos access and does not need Key Vault access; the API stores newly created key secrets in Key Vault. Grant the MCP managed identity read access to `WorkspaceDocuments`, `BillingEntitlements`, and `McpKeys`, and read/write access to `McpUsage`; grant the API managed identity key-container read/write and Key Vault secret set/get/delete permissions. Do not grant the MCP identity workspace, billing, archive, or key writes. Provision the `McpKeys` container partitioned by `/userId` and `McpUsage` partitioned by `/userId`, with a 48-hour TTL on `McpUsage`. The MCP app needs the Functions host storage queue permissions required by the extension.

The app sets MCP webhook authorization to Anonymous so protocol discovery is public and clients do not need a Functions system key. Data tools still require a valid FlowMate API key.
