using Shared.Models;

namespace Services.Repositories;

public sealed class DisabledActivityArchiveRepository(ILogger<DisabledActivityArchiveRepository> logger) : IActivityArchiveRepository
{
    public Task ArchiveAsync(string ownerId, IReadOnlyCollection<FocusSessionRecord> sessions, CancellationToken cancellationToken = default)
    {
        if (sessions.Count > 0)
        {
            logger.LogWarning(
                "Skipped archiving {SessionCount} focus sessions because ConnectionStrings:Storage is not configured.",
                sessions.Count);
        }

        return Task.CompletedTask;
    }
}
