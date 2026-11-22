# FlowMate AI

**Make space for focused work.** FlowMate AI brings tasks, projects, focus sessions and an AI coach together in one workspace, helping you plan your day and understand where your time goes.

Built with .NET, Blazor and Azure, the repository contains the application, its API and integrations, the marketing site, and the infrastructure and tooling used to run them.

## What you can do

- **Plan your work:** organise tasks within projects and work from a Today view.
- **Focus deliberately:** use a Pomodoro-style timer to record focus sessions and breaks.
- **See your progress:** explore reports and project time based on completed focus sessions.
- **Get coaching:** ask the AI coach for help using your workspace context.
- **Connect your tools:** use the read-only Model Context Protocol (MCP) server to retrieve projects and timesheets from a compatible client.
- **Manage your account:** sign in with Auth0, manage billing and integrations, and delete your account from the application.

Some capabilities and history depend on the account plan. See [billing and trials](docs/billing-trial.md) and the [MCP guide](docs/mcp-server.md) for details.

## Architecture

```text
Browser ──► Blazor workspace ──► Functions API ──► Cosmos DB, Blob Storage,
                                                Auth0, Stripe and Azure AI Foundry
MCP client ──► Functions MCP server ──► Cosmos DB
Browser ──► Blazor marketing site ──► workspace
                         Aspire AppHost orchestrates local resources
```

The workspace and marketing site are Blazor WebAssembly apps. The API and MCP server are separate .NET isolated Azure Functions apps. Aspire connects the applications and their dependencies for local development; Azure Cosmos DB stores application data, and Blob Storage archives completed focus records. The API handles authentication, workspace operations, billing and coaching. The MCP server exposes read-only tools using a FlowMate API key.

| Directory | Purpose |
| --- | --- |
| `AppHost/` | Aspire resource graph and local orchestration |
| `Web/`, `Marketing/` | Workspace and marketing frontends |
| `API/`, `MCP/` | HTTP API and MCP Functions hosts |
| `Services/`, `Shared/` | Application services, repositories and shared models |
| `CLI/` | Administrative operations, including database initialisation |
| `ServiceDefaults/` | Shared service configuration and telemetry |
| `Tests/` | API, service and web component tests |
| `infra/`, `docs/` | Azure infrastructure and detailed guides |
| `Formatting/`, `Formatting.CodeFixes/` | Repository formatting analyser and code fixes |

## Getting started

### Prerequisites

- .NET 11 SDK and the workload required to build Blazor WebAssembly.
- Aspire CLI, Azure Functions Core Tools and a container runtime for Redis and Azurite.
- An Azure subscription and signed-in Azure CLI account with permission to create the resources described in [local Aspire startup](docs/deployment.md#local-aspire-startup). The AppHost provisions Azure-backed resources, which can incur charges; local startup is not entirely offline.
- An Auth0 tenant with an API and a single-page application configured for the local gateway. See the [Auth0 setup guide](docs/auth0-azure-setup.md).
- Access to the NuGet feeds configured in [NuGet.config](NuGet.config).

### Configure and run

From the repository root, configure Aspire's Azure subscription and location, and the public Auth0 settings. Replace the placeholders with your own values:

```sh
aspire secret set "Azure:SubscriptionId" "<subscription-id>" --apphost AppHost/AppHost.csproj
aspire secret set "Azure:Location" "<azure-region>" --apphost AppHost/AppHost.csproj
dotnet user-secrets set "Parameters:auth0-authority" "https://<tenant>.auth0.com/" --project AppHost/AppHost.csproj
dotnet user-secrets set "Parameters:auth0-client-id" "<spa-client-id>" --project AppHost/AppHost.csproj
dotnet user-secrets set "Parameters:auth0-audience" "<api-identifier>" --project AppHost/AppHost.csproj
```

Register the local callback, logout and origin URLs in Auth0 as described in the [Auth0 guide](docs/auth0-azure-setup.md#local-configuration-and-public-repository-files). The AppHost also declares an `environment` parameter; provide it through Aspire parameter configuration when prompted.

```sh
dotnet restore FlowMateAI.slnx --configfile NuGet.config
dotnet build FlowMateAI.slnx --no-restore
aspire start --apphost AppHost/AppHost.csproj
```

Open the Aspire dashboard URL printed by the CLI to inspect resources and logs. The development workspace gateway is normally at [https://localhost:7237/frontend/](https://localhost:7237/frontend/); the dashboard also lists the marketing gateway. Initial Azure provisioning can take time. To initialise the database, use the **Ensure database created** command on the `cli` resource in the dashboard; see the [administrative CLI guide](docs/administrative-cli.md).

For local billing, copy [.env.example](.env.example) to `.env` and supply Stripe test-mode values. Coaching, account deletion and other integrations require additional server-side configuration; see the [deployment](docs/deployment.md) and [Auth0](docs/auth0-azure-setup.md) guides. Keep credentials in user secrets, environment variables or Key Vault, never in browser-served settings or source control.

## Build and test

```sh
dotnet build FlowMateAI.slnx
dotnet test FlowMateAI.slnx
```

The solution uses Microsoft Testing Platform, configured in [global.json](global.json). Tests can also be run by project, for example `dotnet test Tests/Services.Tests/Services.Tests.csproj`. See [web test notes](Tests/Web.Tests/README.md) for the component test suite. Full integration and sign-in checks require the external services described above.

## API and integrations

- The API health endpoint is `/api/health`; its HTTP contract is described in [API/openapi.yml](API/openapi.yml).
- Create an MCP key in **Settings → Integrations** and configure a compatible client with the `X-Api-Key` header. The server provides `list_projects`, `get_timesheet` and `get_project_time`. See [MCP server setup](docs/mcp-server.md) for client configuration, history and rate limits.
- The [administrative CLI](docs/administrative-cli.md) provides explicit database and account operations. Review the guide before running commands that modify or delete data.

## Deployment and further documentation

- [Azure deployment and local Aspire configuration](docs/deployment.md)
- [Test environment infrastructure](infra/README.md)
- [Auth0 configuration](docs/auth0-azure-setup.md)
- [Billing and trials](docs/billing-trial.md)
- [Pomodoro timer](docs/POMODORO_TIMER.md)
- [Marketing site](Marketing/README.md) and [branding](docs/branding.md)

The deployment workflow publishes the workspace, marketing site, API and MCP host to configured Azure resources. Provisioning, credentials and environment settings are covered in the linked guides.

## Contributing

Read [AGENTS.md](AGENTS.md) for repository conventions, including British English and application architecture boundaries. Keep changes focused, run the relevant tests, and describe the change and its validation in your pull request.
