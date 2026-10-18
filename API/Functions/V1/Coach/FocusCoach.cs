using API.Security;
using ChrisUsher.Core.Shared;
using Services.Coach;
using Services.Billing;
using Services.Workspaces;
using Services.Users;
using Shared.Models.Coach;

namespace API.Functions.V1.Coach;

public sealed class FocusCoach(Auth0TokenValidator tokens, IFocusCoachService coach, IWorkspaceService workspaces, IBillingService billing, IUserService users)
{

    [Function("FocusCoach")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/coach")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        }

        CoachRequest? input;

        try
        {
            input = await JsonSerializer.DeserializeAsync<CoachRequest>(request.Body, SharedCommon.JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            input = null;
        }

        if (input is null || string.IsNullOrWhiteSpace(input.Prompt) || input.Prompt.Length > 4000)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Enter a message of up to 4,000 characters.", cancellationToken);
        }

        var workspace = await workspaces.GetAsync(userId, cancellationToken);
        var conversation = workspace.Workspace.Conversations.FirstOrDefault(c => c.Id == input.ConversationId);
        FocusCoachAnswer answer;

        try
        {
            answer = await coach.AskAsync(userId, input.Prompt, conversation?.Messages ?? [], cancellationToken);
        }
        catch (CoachQuotaExceededException quota)
        {
            return await HttpFunction.ErrorAsync(request, (HttpStatusCode)429, "prompt_limit", $"You've used {quota.Used} of {quota.Limit} monthly coach prompts.", cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "coach_unavailable", "The focus coach is not configured yet.", cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.GatewayTimeout, "coach_timeout", "The focus coach took too long to respond. Try again.", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "coach_unavailable", "The focus coach could not be reached. Try again in a moment.", cancellationToken);
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

        conversation.Messages.Add(new ChatMessageRecord
        {
            Role = ChatMessageRole.User,
            Text = input.Prompt
        });
        conversation.Messages.Add(new ChatMessageRecord
        {
            Role = ChatMessageRole.Assistant,
            Text = answer.Text
        });
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        workspace.Workspace.Conversations.Remove(conversation);
        workspace.Workspace.Conversations.Insert(0, conversation);

        WorkspaceSaveResult saved;

        try
        {
            saved = await workspaces.SaveAsync(userId, new WorkspaceSaveRequest(workspace.Workspace, workspace.Revision), cancellationToken);
        }
        catch
        {
            await billing.ReleasePromptAsync(userId, answer.PromptMonth, CancellationToken.None);
            throw;
        }

        if (!saved.Succeeded)
        {
            await billing.ReleasePromptAsync(userId, answer.PromptMonth, CancellationToken.None);

            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Conflict, "workspace_conflict", "Your workspace changed on another device. Reload and try again.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new CoachResponse(conversation, saved.Response!.Revision, answer.PromptsUsed, answer.PromptLimit), cancellationToken);

        return response;
    }
}
