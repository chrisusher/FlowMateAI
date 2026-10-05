// Aspire's gateway supplies browser-safe endpoint values in local development.
// The published Static Web Apps bundle uses appsettings.json, so this endpoint
// is optional outside the gateway.
const environment = {};
try {
    const response = await fetch(new URL('_blazor/_configuration', document.baseURI), {
        signal: AbortSignal.timeout(5000)
    });
    if (response.ok) {
        const configuration = await response.json();
        Object.assign(environment, configuration.webAssembly?.environment || {});
    }
} catch {
    // Static Web Apps has no Aspire configuration endpoint. Also let Blazor start
    // if a local gateway does not answer before the request deadline.
}

await Blazor.start(Object.keys(environment).length ? {
    configureRuntime: dotnet => {
        for (const [name, value] of Object.entries(environment)) {
            dotnet.withEnvironmentVariable(name, value);
        }
    }
} : undefined);
