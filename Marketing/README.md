# FlowMate AI marketing site

A standalone Blazor WebAssembly site targeting .NET 11. Build it with `dotnet build Marketing/Marketing.csproj`; publish with `dotnet publish Marketing/Marketing.csproj -c Release`. The static output is under `bin/Release/net11.0/publish/wwwroot`.

Aspire exposes the site at the marketing gateway under `/marketing/` and supplies the app gateway URL to the browser. When hosting the static output independently, set the public `AppUrl` in `wwwroot/appsettings.json` to the deployed app entry URL before publishing. The checked-in value is for local development only. Serve the output at the domain root or at `/marketing/`; adjust the HTML base URL for any other deployment path. Do not place secrets in this browser-visible configuration.

The trial link opens the app pricing page; unauthenticated visitors sign in first and return to pricing. The login link opens the app entry screen. Production hosting and its domain are tracked separately in issue #52.
