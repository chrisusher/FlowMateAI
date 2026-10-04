# Repository guidance for Codex agents

## Start here

- Read this file before changing code. If working inside a subsystem, also read the nearest nested `AGENTS.md`.
- Run `git status --short` first and preserve unrelated user changes.
- Use PowerShell `Get-ChildItem` and `Select-String` for repository search. Do not edit generated `bin/`, `obj/`, `artifacts/`, `TestResults/`, or local secret files.
- Prefer `Scripts/Verify.ps1` over ad-hoc build and test commands.
- Use British English in prose and comments. Follow `.editorconfig`, nullable reference types, file-scoped namespaces, one public type per file, and existing nearby test style.

## Repository map and boundaries

- `API` and `MCP` are isolated Azure Functions hosts.
- `Services` contain business logic, repositories, and service registration.
- `Shared` contains public contracts, DTOs, enums, and common logic.
- `CLI` expose service functionality without the Functions hosts.
- `Web` is the Blazor WebAssembly client; keep components thin and put network calls in typed clients.
- `Infra` contains Bicep and Kubernetes deployment definitions; `Docs` contains operational and feature documentation.

Keep the dependency direction `API/Web/CLI/hosts -> Services -> Shared`. Functions and CLI commands must use services, not repositories directly. Add shared wire contracts to `Shared` instead of redefining them in a host or client.

## Service classes and persistence

- Code whose class or file name ends in `Service` must never reference an Entity Framework context or be coupled to `CosmosClient`.
- This includes importing or naming EF context types or `CosmosClient`, injecting them, storing them in fields or properties, accepting them as parameters, or calling them directly.
- Keep persistence-specific work behind repository or other storage abstractions. `*Service` code may depend on those abstractions, but must not depend on EF contexts or Cosmos SDK clients.
- When changing persistence behavior, preserve this boundary and add or update the abstraction implementation rather than introducing persistence details into a `*Service` class.

## Internal values and enums

- Represent closed sets of application-internal states, modes, roles, periods, stages, and result codes with enums rather than string properties, parameters, or comparisons against string literals.
- Define each enum in its own `.cs` file under `Shared/Enums/`.
- Convert to and from strings only at explicit boundaries such as HTTP/MCP payloads, JSON and database schemas, browser interop, configuration, and third-party APIs. Keep the existing external spelling and casing stable when making these conversions.
- User-facing text, arbitrary user data, identifiers, URLs, configuration keys, protocol field names, and values owned by external systems are not internal enum candidates.
- In JavaScript, where the language has no native enums, represent closed internal sets with a frozen named constant object rather than repeating string literals.

## Verification

Use the repository script from the root:

```powershell
pwsh -NoProfile -File Scripts/Verify.ps1 -Target Build
pwsh -NoProfile -File Scripts/Verify.ps1 -Target Component
pwsh -NoProfile -File Scripts/Verify.ps1 -Target Backend
```

`Build` and `Component` are the default local checks. `Backend` start Docker-backed test infrastructure and may require secrets or environment variables. Use `-NoRestore` only after the isolated restore has completed.

For a focused change, run the smallest relevant target and report what was not run. A failed prerequisite is a diagnostic result, not a reason to edit unrelated configuration.