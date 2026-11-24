using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Enums;
using Shared.Models;

namespace Services.Repositories;

/// <summary>Writes project records in the user's Projects partition.</summary>
public sealed class ProjectWriteRepository(DatabaseContext database) : IProjectWriteRepository
{
    public async Task<ProjectWriteResult> GetAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        if (!await IsReadyAsync(userId, cancellationToken))
        {
            return new(null, null, ProjectChangeRejection.StorageNotReady);
        }

        var document = await database.Projects.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);

        return document?.Project is null
            ? new(null, null, ProjectChangeRejection.NotFound)
            : new(document.Project, document.ETag, ProjectChangeRejection.None);
    }

    public async Task<ProjectWriteResult> CreateAsync(string userId, ProjectRecord project, BillingPlan plan, CancellationToken cancellationToken = default)
    {
        if (!await IsReadyAsync(userId, cancellationToken))
        {
            return new(null, null, ProjectChangeRejection.StorageNotReady);
        }

        // The index and project share a container and partition, so EF's Cosmos batch
        // atomically reserves a slot and creates the record. A conflicting index ETag
        // rejects competing creates/restores, including those from other processes.
        var index = await database.ProjectIndexes.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == "project-index", cancellationToken);

        if (index is null)
        {
            return new(null, null, ProjectChangeRejection.StorageNotReady);
        }

        var existing = await database.Projects.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == project.Id, cancellationToken);

        if (existing is not null)
        {
            var matches = existing.Project is not null && JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(existing.Project, SharedCommon.JsonOptions),
                JsonSerializer.SerializeToElement(project, SharedCommon.JsonOptions));

            return matches
                ? new(existing.Project, existing.ETag, ProjectChangeRejection.None)
                : new(null, null, ProjectChangeRejection.IdConflict);
        }

        var limit = plan == BillingPlan.Pro ? 25 : 3;

        if (index.ActiveCount >= limit)
        {
            return new(null, null, ProjectChangeRejection.ProjectLimit);
        }

        if (index.TotalCount >= 1000)
        {
            return new(null, null, ProjectChangeRejection.Capacity);
        }

        var document = new WorkspaceProjectDocument
        {
            Id = project.Id,
            UserId = userId,
            Position = index.NextPosition,
            Project = project
        };
        database.Projects.Add(document);
        index.ActiveCount++;
        index.TotalCount++;
        index.NextPosition++;
        var previousTransactionBehaviour = database.Database.AutoTransactionBehavior;
        database.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;

        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return new(document.Project, document.ETag, ProjectChangeRejection.None);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            return new(null, null, ProjectChangeRejection.Conflict);
        }
        finally
        {
            database.Database.AutoTransactionBehavior = previousTransactionBehaviour;
        }
    }

    public async Task<ProjectWriteResult> RenameAsync(string userId, string id, string name, string revision, CancellationToken cancellationToken = default)
    {
        if (!await IsReadyAsync(userId, cancellationToken))
        {
            return new(null, null, ProjectChangeRejection.StorageNotReady);
        }

        var document = await database.Projects.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);

        if (document?.Project is null)
        {
            return new(null, null, ProjectChangeRejection.NotFound);
        }

        if (document.Project.IsArchived)
        {
            return new(null, null, ProjectChangeRejection.AlreadyArchived);
        }

        if (string.IsNullOrEmpty(revision) || !string.Equals(document.ETag, revision, StringComparison.Ordinal))
        {
            return new(null, null, ProjectChangeRejection.Conflict);
        }

        document.Project.Name = name;

        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return new(document.Project, document.ETag, ProjectChangeRejection.None);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            return new(null, null, ProjectChangeRejection.Conflict);
        }
    }

    private Task<bool> IsReadyAsync(string userId, CancellationToken cancellationToken) => database.Workspaces.AsNoTracking()
        .AnyAsync(item => item.UserId == userId && item.Id == "workspace" && item.ProjectsAuthoritative, cancellationToken);
}
