using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Shared.Contracts;

namespace MCP.Functions;

public sealed class GetProjectTime(IMcpToolInvocation invocation)
{
    [Function(nameof(GetProjectTime))]
    public Task<IMcpToolResponse> Run(
        [McpToolTrigger("get_project_time", "Read a project's total focused minutes, session count, and daily breakdown.")]
        ToolInvocationContext context,
        [McpToolProperty("projectId", "Project ID from list_projects.", true)] string projectId,
        [McpToolProperty("period", "Reporting period: day, week, or month.", true)] string period,
        [McpToolProperty("date", "ISO calendar date (YYYY-MM-DD); defaults to today in the workspace time zone.")] string? date,
        CancellationToken cancellationToken)
        => invocation.InvokeAsync(context, period, date, null, projectId, false, cancellationToken);
}
