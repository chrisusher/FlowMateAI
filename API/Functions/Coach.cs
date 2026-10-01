using System.Net;
using System.Text.Json;
using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Coach;
using Services.Workspaces;
using Shared.Contracts;
using Shared.Models;

namespace API.Functions;

public sealed class Coach(Auth0TokenValidator tokens, IFocusCoachService coach, IWorkspaceService workspaces)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("FocusCoach")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/coach")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        var principal = await tokens.ValidateAsync(authorization, cancellationToken);
        var userId = principal?.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return await ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);

        CoachRequest? input;
        try
        { input = await JsonSerializer.DeserializeAsync<CoachRequest>(request.Body, JsonOptions, cancellationToken); }
        catch (JsonException) { input = null; }

        if (input is null || string.IsNullOrWhiteSpace(input.Prompt) || input.Prompt.Length > 4000)
            return await ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Enter a message of up to 4,000 characters.", cancellationToken);

        var workspace = await workspaces.GetAsync(userId, cancellationToken);
        var conversation = workspace.Workspace.Conversations.FirstOrDefault(c => c.Id == input.ConversationId);
        FocusCoachAnswer answer;
        var history = conversation?.Messages ?? [];
        try
        { answer = await coach.AskAsync(userId, input.Prompt, history, cancellationToken); }
        catch (CoachQuotaExceededException quota)
        {
            return await ErrorAsync(request, (HttpStatusCode)429, "prompt_limit", $"You've used {quota.Used} of {quota.Limit} monthly coach prompts.", cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return await ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "coach_unavailable", "The focus coach is not configured yet.", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return await ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "coach_unavailable", "The focus coach could not be reached. Try again in a moment.", cancellationToken);
        }

        if (conversation is null)
        {
            conversation = new ConversationRecord
            {
                Title = input.Prompt.Length > 38 ? input.Prompt[..38] + "…" : input.Prompt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            workspace.Workspace.Conversations.Insert(0, conversation);
        }
        conversation.Messages.Add(new ChatMessageRecord { Role = "user", Text = input.Prompt });
        conversation.Messages.Add(new ChatMessageRecord { Role = "assistant", Text = answer.Text });
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        workspace.Workspace.Conversations.Remove(conversation);
        workspace.Workspace.Conversations.Insert(0, conversation);
        var saved = await workspaces.SaveAsync(userId, new WorkspaceSaveRequest(workspace.Workspace, workspace.Revision), cancellationToken);

        if (!saved.Succeeded)
            return await ErrorAsync(request, HttpStatusCode.Conflict, "workspace_conflict", "Your workspace changed on another device. Reload and try again.", cancellationToken);

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new CoachResponse(conversation, saved.Response!.Revision, answer.PromptsUsed, answer.PromptLimit), cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData request, HttpStatusCode status, string code, string message, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiError(code, message), cancellationToken);
        return response;
    }
}
