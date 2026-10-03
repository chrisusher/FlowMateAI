using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Services.Reporting;

namespace MCP;

public sealed class ReportingTools(IMcpToolInvocation invocation)
{
    [Function(nameof(ListProjects))]
    public Task<object> ListProjects([McpToolTrigger("list_projects", "List this account's projects and time zone, available history, and MCP rate policy.")] ToolInvocationContext context, CancellationToken cancellationToken)
        => invocation.InvokeAsync(context, "month", null, null, null, true, cancellationToken);

    [Function(nameof(GetTimesheet))]
    public Task<object> GetTimesheet(
        [McpToolTrigger("get_timesheet", "Read focus entries and complete day, project, and overall totals for a day, week, or month.")]
        ToolInvocationContext context,
        [McpToolProperty("period", "Reporting period: day, week, or month.", true)] string period,
        [McpToolProperty("date", "ISO calendar date (YYYY-MM-DD); defaults to today in the workspace time zone.")] string? date,
        [McpToolProperty("cursor", "Optional pagination cursor returned by the previous page.")] string? cursor,
        CancellationToken cancellationToken)
        => invocation.InvokeAsync(context, period, date, cursor, null, false, cancellationToken);

    [Function(nameof(GetProjectTime))]
    public Task<object> GetProjectTime(
        [McpToolTrigger("get_project_time", "Read a project's total focused minutes, session count, and daily breakdown.")]
        ToolInvocationContext context,
        [McpToolProperty("projectId", "Project ID from list_projects.", true)] string projectId,
        [McpToolProperty("period", "Reporting period: day, week, or month.", true)] string period,
        [McpToolProperty("date", "ISO calendar date (YYYY-MM-DD); defaults to today in the workspace time zone.")] string? date,
        CancellationToken cancellationToken)
        => invocation.InvokeAsync(context, period, date, null, projectId, false, cancellationToken);
}
