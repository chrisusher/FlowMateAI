using Shared.Models;

namespace Services.Repositories;

public interface IActivityArchiveRepository
{
    Task ArchiveAsync(string ownerId, IReadOnlyCollection<FocusSessionRecord> sessions, CancellationToken cancellationToken = default);
}
