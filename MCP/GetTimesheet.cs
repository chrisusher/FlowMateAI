using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Shared.Contracts;

namespace MCP;

public sealed class GetTimesheet(IMcpToolInvocation invocation)
{
    [Function(nameof(GetTimesheet))]
    public Task<IMcpToolResponse> Run(
        [McpToolTrigger("get_timesheet", "Read focus entries and complete day, project, and overall totals for a day, week, or month.")]
        ToolInvocationContext context,
        [McpToolProperty("period", "Reporting period: day, week, or month.", true)] string period,
        [McpToolProperty("date", "ISO calendar date (YYYY-MM-DD); defaults to today in the workspace time zone.")] string? date,
        [McpToolProperty("cursor", "Optional pagination cursor returned by the previous page.")] string? cursor,
        CancellationToken cancellationToken)
        => invocation.InvokeAsync(context, period, date, cursor, null, false, cancellationToken);
}
