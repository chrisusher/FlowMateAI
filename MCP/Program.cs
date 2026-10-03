using MCP;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Services;
using Services.Mcp;
using Services.Reporting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.Services.AddMcpServices(builder.Configuration);
builder.Services.AddScoped<IMcpToolInvocation, McpToolInvocation>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("FlowMateAI.MCP"))
    .WithTracing(t => t.AddHttpClientInstrumentation().AddOtlpExporter())
    .WithMetrics(m => m.AddMeter("FlowMateAI.MCP").AddOtlpExporter());

builder.Build().Run();
