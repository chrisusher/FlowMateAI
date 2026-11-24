using API.Security;
using Services.ApiRequests;
using Services.Repositories;
using Services.Users;
using Services.Workspaces;

namespace API.Functions.V1.Projects;

public sealed class CreateProject(Auth0TokenValidator tokens, IUserService users, IAuthenticatedApiRateLimiter limiter, IProjectWriteService projects)
{
    [Function("CreateProject")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/projects")] HttpRequestData request, CancellationToken cancellationToken) =>
        ProjectWriteHttp.ExecuteAsync<ProjectCreateRequest>(request, tokens, users, limiter, CreateAsync, HttpStatusCode.Created, cancellationToken);

    private Task<ProjectWriteResult> CreateAsync(string userId, ProjectCreateRequest input, CancellationToken cancellationToken)
    {
        if (input.Id is null || input.Name is null || input.Color is null || input.CreatedAt is null)
        {
            return Task.FromResult(new ProjectWriteResult(null, null, ProjectChangeRejection.InvalidInput));
        }

        return projects.CreateAsync(userId, new ProjectRecord
        {
            Id = input.Id,
            Name = input.Name,
            Color = input.Color,
            CreatedAt = input.CreatedAt.Value
        }, cancellationToken);
    }
}
