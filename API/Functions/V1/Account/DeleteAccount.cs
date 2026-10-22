using API.Security;
using ChrisUsher.Core.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Users;
using Shared.Contracts;

namespace API.Functions.V1.Account;

public sealed class DeleteAccount(
    Auth0TokenValidator tokens,
    IUserService users,
    IAccountDeletionService deletion)
{
    private const string Confirmation = "DELETE MY ACCOUNT";

    [Function(nameof(DeleteAccount))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/account")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var (userId, email) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token with a verified email is required.", cancellationToken);
        }

        AccountDeletionRequest? body;

        try
        {
            body = await JsonSerializer.DeserializeAsync<AccountDeletionRequest>(request.Body, SharedCommon.JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null
            || !string.Equals(body.Confirmation, Confirmation, StringComparison.Ordinal)
            || !string.Equals(body.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "confirmation_required",
                $"Enter your account email and type {Confirmation} exactly to confirm deletion.", cancellationToken);
        }

        var result = await deletion.DeleteAsync(userId, cancellationToken);

        if (!result.Succeeded)
        {
            var status = result.Stage == Shared.Enums.AccountDeletionStage.Billing
                ? HttpStatusCode.Conflict
                : HttpStatusCode.ServiceUnavailable;
            var code = result.Stage switch
            {
                Shared.Enums.AccountDeletionStage.Billing => "billing_not_cancelled",
                Shared.Enums.AccountDeletionStage.ApplicationData => "application_data_deletion_incomplete",
                Shared.Enums.AccountDeletionStage.Identity => "identity_deletion_incomplete",
                _ => "account_deletion_failed"
            };

            return await HttpFunction.ErrorAsync(request, status, code, result.Message, cancellationToken);
        }

        return request.CreateResponse(HttpStatusCode.NoContent);
    }
}
