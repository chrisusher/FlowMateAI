// configureRuntime must be supplied to Blazor.start: a beforeStart initializer
// runs too late to replace that callback in standalone WebAssembly.
// Aspire's gateway provides this endpoint locally. Static Web Apps has no such
// endpoint, so production falls back to the public settings generated at build time.
let environment;

try {
    const response = await fetch(new URL('_blazor/_configuration', document.baseURI));

    if (response.ok) {
        const configuration = await response.json();
        environment = configuration.webAssembly?.environment;
    }
} catch {
    // The static host may return the SPA fallback page for this gateway-only route.
}

if (!environment || Object.keys(environment).length === 0) {
    const response = await fetch(new URL('appsettings.json', document.baseURI));

    if (!response.ok) {
        throw new Error(`Unable to load application settings: ${response.status}`);
    }

    const settings = await response.json();
    environment = {};

    const addEnvironmentVariables = (value, prefix = '') => {
        for (const [key, child] of Object.entries(value)) {
            const name = prefix ? `${prefix}__${key}` : key;

            if (child !== null && typeof child === 'object' && !Array.isArray(child)) {
                addEnvironmentVariables(child, name);
            } else if (child !== null && child !== undefined) {
                environment[name] = String(child);
            }
        }
    };

    addEnvironmentVariables(settings);
}

if (Object.keys(environment).length === 0) {
    throw new Error('No WebAssembly runtime environment configuration was provided.');
}

await Blazor.start({
    configureRuntime: dotnet => {
        for (const [name, value] of Object.entries(environment)) {
            dotnet.withEnvironmentVariable(name, value);
        }
    }
});
