// configureRuntime must be supplied to Blazor.start: a beforeStart initializer
// runs too late to replace that callback in standalone WebAssembly.
let environment;

try {
    const response = await fetch(new URL('_blazor/_configuration', document.baseURI));
    if (response.ok) {
        const configuration = await response.json();
        environment = configuration.webAssembly?.environment;
    }
} catch {
    // Standalone Static Web Apps builds use ApiBaseUrl from appsettings.json.
}

if (!environment) {
    try {
        const response = await fetch(new URL('appsettings.json', document.baseURI));
        
        if (response.ok) {
            const configuration = await response.json();
            
            if (configuration.ApiBaseUrl) {
                environment = { 
                    ApiBaseUrl: configuration.ApiBaseUrl 
                };
            }
        }
    } catch {
        // The gateway configuration remains authoritative when it is available.
    }
}

if (environment) {
    await Blazor.start({
        configureRuntime: dotnet => {
            for (const [name, value] of Object.entries(environment)) {
                dotnet.withEnvironmentVariable(name, value);
            }
        }
    });
} else {
    await Blazor.start();
}
