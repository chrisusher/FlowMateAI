# Repository guidance for Codex agents

## Language and spelling

- This is a British English repository. Use British English spelling and conventions in documentation, comments, and user-facing text where possible. Preserve established spelling at external or compatibility boundaries.

## C# formatting

- Put opening and closing braces on their own lines.
- Put each property in an object initialiser on its own line, including nested initialisers.
- Follow `.editorconfig`; check object initialisers manually when the formatter leaves them on one line.

## Dependency injection

- Inject application dependencies, such as services, repositories and collaborators, through constructors rather than constructing them with `new` inside application classes.
- Register dependencies with appropriate lifetimes in the dependency injection container. Constructing data objects, value objects and exceptions directly is appropriate; composition roots and tests may construct dependencies explicitly.

## Azure Functions

- Define each HTTP-triggered function in its own `.cs` file when creating Azure Functions.
- Place HTTP function files in folders matching their route segments, including route parameters.
- Move duplicated function logic into shared base classes or other shared abstractions; do not use partial classes to split functions.

## Service classes and persistence

- Code whose class or file name ends in `Service` must never reference an Entity Framework context or be coupled to `CosmosClient`.
- This includes importing or naming EF context types or `CosmosClient`, injecting them, storing them in fields or properties, accepting them as parameters, or calling them directly.
- Keep persistence-specific work behind repository or other storage abstractions. `*Service` code may depend on those abstractions, but must not depend on EF contexts or Cosmos SDK clients.
- When changing persistence behavior, preserve this boundary and add or update the abstraction implementation rather than introducing persistence details into a `*Service` class.

## Cosmos document design

- Store independently changing or growing workspace collections as individual documents in purpose-specific containers, not as arrays in a shared workspace document. Choose and document partition keys for the access patterns and user isolation.
- Design record-level concurrency, retries, migration and deletion together; moving arrays alone does not remove contention if every change still writes one shared revision document.

## Internal values and enums

- Represent closed sets of application-internal states, modes, roles, periods, stages, and result codes with enums rather than string properties, parameters, or comparisons against string literals.
- Define each enum in its own `.cs` file under `Shared/Enums/`.
- Convert to and from strings only at explicit boundaries such as HTTP/MCP payloads, JSON and database schemas, browser interop, configuration, and third-party APIs. Keep the existing external spelling and casing stable when making these conversions.
- User-facing text, arbitrary user data, identifiers, URLs, configuration keys, protocol field names, and values owned by external systems are not internal enum candidates.
- In JavaScript, where the language has no native enums, represent closed internal sets with a frozen named constant object rather than repeating string literals.

## Cosmos DB storage

- All Cosmos DB reads and writes must go through Entity Framework Core repositories. Do not use the Cosmos SDK directly for application or administrative operations.

- Store application data as typed, queryable Cosmos properties and native objects or arrays. Do not serialise application objects into JSON string properties.
- Exceptions require a documented technical necessity; retain backwards-compatible reads and provide a reviewed migration or repair path for existing documents when changing storage format.
- Administrative data repairs must provide a preview, explicit write opt-in, account scope, concurrency protection and a recoverable backup.
