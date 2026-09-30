// configureRuntime must be supplied to Blazor.start: a beforeStart initializer
// runs too late to replace that callback in standalone WebAssembly.
const response = await fetch(new URL('_blazor/_configuration', document.baseURI));
if (!response.ok) {
    throw new Error(`Unable to load gateway configuration: ${response.status}`);
}

const configuration = await response.json();
const environment = configuration.webAssembly?.environment;
if (!environment) {
    throw new Error('The gateway did not provide WebAssembly environment configuration.');
}

await Blazor.start({
    configureRuntime: dotnet => {
        for (const [name, value] of Object.entries(environment)) {
            dotnet.withEnvironmentVariable(name, value);
        }
    }
});
