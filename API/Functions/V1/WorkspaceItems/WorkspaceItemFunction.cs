using API.Security;
using Services.Users;

namespace API.Functions.V1.WorkspaceItems;

public abstract class WorkspaceItemFunction(Auth0TokenValidator tokens, IUserService users)
{
    protected async Task<HttpResponseData> ListAsync<T>(HttpRequestData request, Func<string, CancellationToken, Task<IReadOnlyList<T>>> list, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var records = await list(userId, cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(records, cancellationToken);

        return response;
    }

    protected async Task<HttpResponseData> GetAsync<T>(HttpRequestData request, string id, Func<string, string, CancellationToken, Task<T?>> get, CancellationToken cancellationToken) where T : class
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var record = await get(userId, id, cancellationToken);

        if (record is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.NotFound, "not_found", "The record was not found.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(record, cancellationToken);

        return response;
    }
}
