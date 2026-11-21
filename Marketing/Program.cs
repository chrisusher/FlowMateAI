using Marketing;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
((IConfigurationBuilder)builder.Configuration).AddEnvironmentVariables();
builder.RootComponents.Add<App>("#app");
await builder.Build().RunAsync();
