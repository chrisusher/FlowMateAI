using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Shared.Contracts;

namespace MCP;

public sealed class ListProjects(IMcpToolInvocation invocation)
{
    [Function(nameof(ListProjects))]
    public Task<IMcpToolResponse> Run([McpToolTrigger("list_projects", "List this account's projects and time zone, available history, and MCP rate policy.")] ToolInvocationContext context, CancellationToken cancellationToken)
        => invocation.InvokeAsync(context, "month", null, null, null, true, cancellationToken);
}
