# Web component tests

Run from the repository root:

```powershell
dotnet test --project Tests/Web.Tests/Web.Tests.csproj
```

The project uses bUnit and NUnit on .NET 10. `global.json` selects Microsoft.Testing.Platform for the NUnit runner without pinning the SDK version. Each test gets a fresh fixture and bUnit context through `FixtureLifeCycle(LifeCycle.InstancePerTestCase)`.

Tests cover all 39 Razor components: cards, dialogs, navigation, pages, layouts, the router, and the app root. `_Imports.razor` contains directives and is not a renderable component. Tests assert displayed data, user actions, persistence calls, route selection, shared state, and disposal rather than storing full-markup snapshots.

Asynchronous checks use bUnit's `WaitForState` before NUnit assertions, so intermediate states do not record assertion failures while a render or timer transition is still pending.

`WorkspaceComponentTest` registers the application's real stores and managers with an in-memory HTTP handler and bUnit JavaScript interop doubles. Radzen DOM measurements use loose interop; authentication and workspace storage have explicit return values. No browser, JavaScript execution, credentials, or live backend are required.

Reference: [bUnit JavaScript interop doubles](https://bunit.dev/docs/test-doubles/emulating-ijsruntime).
