# Repository guidance for Codex agents

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
