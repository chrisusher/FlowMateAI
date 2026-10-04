// using MCP;
// using Microsoft.Azure.Functions.Worker.Builder;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using OpenTelemetry.Metrics;
// using OpenTelemetry.Resources;
// using OpenTelemetry.Trace;
// using Services;
// using Services.Mcp;
// using Services.Reporting;

// var builder = FunctionsApplication.CreateBuilder(args);
// builder.ConfigureFunctionsWebApplication();
// builder.Services.AddMcpServices(builder.Configuration);
// builder.Services.AddScoped<IMcpToolInvocation, McpToolInvocation>();

// builder.Services.AddOpenTelemetry()
//     .ConfigureResource(r => r.AddService("FlowMateAI.MCP"))
//     .WithTracing(t => t.AddHttpClientInstrumentation().AddOtlpExporter())
//     .WithMetrics(m => m.AddMeter("FlowMateAI.MCP").AddOtlpExporter());

// builder.Build().Run();

using MCP;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Services;
using Services.Mcp;
using Services.Reporting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(app =>
    {
        app.Use(next => new FunctionExecutionDelegate(async context =>
        {
            var httpContext = context.GetHttpContext();

            if (httpContext is not null)
            {
                var origin = httpContext.Request.Headers.Origin.ToString();
                var allowedOrigins = context.InstanceServices.GetRequiredService<IConfiguration>()
                    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

                if (!string.IsNullOrWhiteSpace(origin) && allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    httpContext.Response.Headers["Access-Control-Allow-Origin"] = origin;
                }

                httpContext.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, If-Match";
                httpContext.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
                httpContext.Response.Headers["Vary"] = "Origin";

                if (httpContext.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    httpContext.Response.StatusCode = StatusCodes.Status204NoContent;

                    return;
                }
            }

            await next(context);
        }));
    })
    .ConfigureServices((context, services) =>
    {
        // Configure logging to suppress Azure Storage noise
        services.Configure<LoggerFilterOptions>(options =>
        {
            options.AddFilter("Azure.Storage.Blobs", LogLevel.Error);
            options.AddFilter("Azure.Storage.Common", LogLevel.Error);
            options.AddFilter("Azure.Core", LogLevel.Error);
            options.AddFilter("Azure", LogLevel.Error);
            options.AddFilter("Microsoft.Azure.Storage", LogLevel.Error);
            options.AddFilter("Microsoft.Azure.WebJobs.Host.Blobs", LogLevel.Error);
            options.AddFilter("Microsoft.Azure.WebJobs.Extensions.Storage", LogLevel.Error);
        });

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Backend"))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new AlwaysOnSampler())
                    .AddSource("Services.Clients.MarketDataClient")
                    .AddHttpClientInstrumentation();

                tracing.AddOtlpExporter();
            });

        var configuration = context.Configuration;
        services.AddServices(configuration);

        services.AddMcpServices(configuration);
        services.AddScoped<IMcpToolInvocation, McpToolInvocation>();
    })
    .Build();

host.Run();
