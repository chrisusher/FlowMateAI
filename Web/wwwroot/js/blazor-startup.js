// Aspire's gateway supplies browser-safe endpoint values in local development.
// The published Static Web Apps bundle uses appsettings.json, so this endpoint
// is optional outside the gateway.
const environment = {};
try {
    const response = await fetch(new URL('_blazor/_configuration', document.baseURI));
    if (response.ok) {
        const configuration = await response.json();
        Object.assign(environment, configuration.webAssembly?.environment || {});
    }
} catch {
    // Static Web Apps does not expose Aspire's runtime configuration endpoint.
}

await Blazor.start(Object.keys(environment).length ? {
    configureRuntime: dotnet => {
        for (const [name, value] of Object.entries(environment)) {
            dotnet.withEnvironmentVariable(name, value);
        }
    }
} : undefined);
