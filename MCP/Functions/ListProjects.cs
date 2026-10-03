using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace MCP.Functions;

public sealed class ListProjects(IMcpToolInvocation invocation)
{
    [Function(nameof(ListProjects))]
    public Task<IMcpToolResponse> Run([McpToolTrigger("list_projects", "List this account's projects and time zone, available history, and MCP rate policy.")] ToolInvocationContext context, CancellationToken cancellationToken)
        => invocation.InvokeAsync(context, ReportPeriod.Month.ToWireValue(), null, null, null, true, cancellationToken);
}
