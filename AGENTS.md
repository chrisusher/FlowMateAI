# Repository guidance for Codex agents

## Service classes and persistence

- Code whose class or file name ends in `Service` must never reference an Entity Framework context or be coupled to `CosmosClient`.
- This includes importing or naming EF context types or `CosmosClient`, injecting them, storing them in fields or properties, accepting them as parameters, or calling them directly.
- Keep persistence-specific work behind repository or other storage abstractions. `*Service` code may depend on those abstractions, but must not depend on EF contexts or Cosmos SDK clients.
- When changing persistence behavior, preserve this boundary and add or update the abstraction implementation rather than introducing persistence details into a `*Service` class.
