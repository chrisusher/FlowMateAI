using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shared.Exceptions;
using Services.Billing;
using Services.Workspaces;

namespace Services.Coach;

public sealed class FoundryFocusCoachService(
    IConfiguration configuration,
    IHttpClientFactory clients,
    IWorkspaceService workspaces,
    IBillingService billing) : IFocusCoachService
{
    public async Task<FocusCoachAnswer> AskAsync(string userId, string prompt, IReadOnlyList<Shared.Models.ChatMessageRecord> history, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4000)
        {
            throw new ArgumentException("Enter a message of up to 4,000 characters.", nameof(prompt));
        }
        var endpoint = configuration["Foundry:Endpoint"]?.TrimEnd('/');
        var deployment = configuration["Foundry:Deployment"];
        var key = configuration["Foundry:ApiKey"];
        var apiVersion = configuration["Foundry:ApiVersion"] ?? "2024-10-21";

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("The Azure AI Foundry coach is not configured.");
        }

        var (allowed, used, limit) = await billing.ConsumePromptAsync(userId, cancellationToken);

        if (!allowed)
        {
            throw new CoachQuotaExceededException(used, limit);
        }

        var result = await workspaces.GetAsync(userId, cancellationToken);
        var workspace = result.Workspace;
        var now = DateTimeOffset.UtcNow;
        var recent = workspace.Sessions.Where(s => s.EndedAt >= now.AddDays(-31)).ToList();
        var context = new
        {
            localDate = LocalDate(now, workspace.TimeZone),
            timeZone = workspace.TimeZone,
            projects = workspace.Projects.Select(p => new { p.Name, tasks = workspace.Tasks.Where(t => t.ProjectId == p.Id).Select(t => new { t.Title, priority = t.Priority.ToString(), t.IsComplete, t.PlannedToday, dueDate = t.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }) }),
            focusMinutesToday = MinutesForLocalDay(recent, now, workspace.TimeZone),
            focusMinutesLast7Days = recent.Where(s => s.EndedAt >= now.AddDays(-7)).Sum(s => s.FocusMinutes),
            focusMinutesLast30Days = recent.Sum(s => s.FocusMinutes)
        };

        var url = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("api-key", key);
        var messages = new List<object>
        {
            new { role = "system", content = "You are FlowMate, a thoughtful, practical focus coach. Use only the user's supplied projects, tasks and focus totals. You may suggest priorities and reflect on day/week/month results, but you must never claim to edit or change data. Keep replies warm, concise, and grounded in the supplied context. If the context lacks information, say so. The last user message contains the current workspace context." }
        };
        messages.AddRange(history.TakeLast(10).Where(m => m.Role is "user" or "assistant").Select(m => (object)new { role = m.Role, content = m.Text }));
        messages.Add(new { role = "user", content = $"Workspace context (JSON): {JsonSerializer.Serialize(context)}\n\nUser message: {prompt}" });
        request.Content = new StringContent(JsonSerializer.Serialize(new { temperature = 0.35, max_tokens = 500, messages }), Encoding.UTF8, "application/json");
        using var response = await clients.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var text = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new HttpRequestException("Foundry returned an empty coach response.");
        }

        return new(text.Trim(), used, limit);
    }

    private static string LocalDate(DateTimeOffset instant, string timeZone)
    {
        try
        {

            return TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {

            return instant.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private static int MinutesForLocalDay(IEnumerable<Shared.Models.FocusSessionRecord> sessions, DateTimeOffset now, string timeZone)
    {
        var day = LocalDate(now, timeZone);

        return sessions.Where(s => LocalDate(s.StartedAt, timeZone) == day).Sum(s => s.FocusMinutes);
    }
}
