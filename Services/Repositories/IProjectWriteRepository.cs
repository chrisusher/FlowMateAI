using Shared.Enums;
using Shared.Models;

namespace Services.Repositories;

public interface IProjectWriteRepository
{
    Task<ProjectWriteResult> GetAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<ProjectWriteResult> CreateAsync(string userId, ProjectRecord project, BillingPlan plan, CancellationToken cancellationToken = default);
    Task<ProjectWriteResult> RenameAsync(string userId, string id, string name, string revision, CancellationToken cancellationToken = default);
}
