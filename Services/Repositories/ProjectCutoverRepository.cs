using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

/// <summary>Prepares and verifies one user's project records for a guarded cutover.</summary>
public sealed class ProjectCutoverRepository(DatabaseContext database)
{
    public async Task<ProjectCutoverPreview> PreviewAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var workspace = await ReadWorkspaceAsync(userId, cancellationToken);

        if (workspace.ProjectsAuthoritative || workspace.PendingProjectArchive is not null || workspace.PendingTaskOperation is not null)
        {
            throw new InvalidOperationException("Projects have already been cut over for this account.");
        }

        if (string.IsNullOrWhiteSpace(workspace.ETag))
        {
            throw new InvalidOperationException("A workspace revision is required for project cutover.");
        }

        var projects = WorkspaceStorageFormat.Read(workspace).Projects;
        ValidateProjects(projects);
        await CheckDestinationAsync(userId, projects, cancellationToken);
        var indexes = await database.ProjectIndexes.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(cancellationToken);

        if (indexes.Count > 1)
        {
            throw new InvalidOperationException("Multiple project indexes exist for this account; no records were written.");
        }

        var index = indexes.SingleOrDefault();

        if (index is not null && (index.ActiveCount != projects.Count(project => !project.IsArchived) ||
            index.TotalCount != projects.Count || index.NextPosition != projects.Count || projects.Any(project => project.Id == index.Id)))
        {
            throw new InvalidOperationException("The existing project index differs from the workspace; no records were written.");
        }

        return new(userId, workspace.ETag, projects.Count, projects.Count(project => !project.IsArchived));
    }

    /// <summary>
    /// Copies missing project documents, verifies them and conditionally changes the
    /// authority marker. Call only with all old and new workspace writers stopped and
    /// after an independent, recoverable backup has been made.
    /// </summary>
    public async Task ApplyAsync(string userId, string expectedRevision, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(userId, cancellationToken);

        if (!string.Equals(preview.Revision, expectedRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workspace changed after preview; back it up and preview again.");
        }

        var source = await ReadWorkspaceAsync(userId, cancellationToken);
        var projects = WorkspaceStorageFormat.Read(source).Projects;

        if (!string.Equals(source.ETag, expectedRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workspace changed during project cutover.");
        }

        var existing = await database.Projects.AsNoTracking().Where(item => item.UserId == userId).Select(item => item.Id).ToListAsync(cancellationToken);
        var existingIds = existing.ToHashSet(StringComparer.Ordinal);

        for (var position = 0; position < projects.Count; position++)
        {
            var project = projects[position];

            if (existingIds.Contains(project.Id))
            {
                continue;
            }

            var record = new WorkspaceProjectDocument
            {
                Id = project.Id,
                UserId = userId,
                Position = position,
                Project = project
            };
            database.Projects.Add(record);
            await database.SaveChangesAsync(cancellationToken);
            database.ChangeTracker.Clear();
        }

        await CheckDestinationAsync(userId, projects, cancellationToken, requireComplete: true);
        var indexes = await database.ProjectIndexes.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var index = indexes.SingleOrDefault();

        if (index is null)
        {
            string indexId;
            var salt = 0;

            do
            {
                var identity = System.Text.Encoding.UTF8.GetBytes($"{userId}\0{expectedRevision}\0{salt++}");
                indexId = $"project-index-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(identity))}";
            }
            while (projects.Any(project => project.Id == indexId));

            index = new ProjectIndexDocument
            {
                Id = indexId,
                UserId = userId,
                ActiveCount = preview.ActiveCount,
                TotalCount = preview.ProjectCount,
                NextPosition = preview.ProjectCount
            };
            database.ProjectIndexes.Add(index);
            await database.SaveChangesAsync(cancellationToken);
        }
        else if (index.ActiveCount != preview.ActiveCount || index.TotalCount != preview.ProjectCount || index.NextPosition != preview.ProjectCount)
        {
            throw new InvalidOperationException("The existing project index differs from the preview; no cutover was made.");
        }

        database.ChangeTracker.Clear();
        var current = await database.Workspaces.SingleAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);

        if (current.ProjectsAuthoritative || !string.Equals(current.ETag, expectedRevision, StringComparison.Ordinal) ||
            !Equivalent(WorkspaceStorageFormat.Read(current).Projects, projects))
        {
            throw new InvalidOperationException("The workspace changed during project cutover; no cutover was made.");
        }

        current.ProjectIndexId = index.Id;
        current.ProjectsAuthoritative = true;
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkspaceDocument> ReadWorkspaceAsync(string userId, CancellationToken cancellationToken) =>
        await database.Workspaces.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken)
            ?? throw new InvalidOperationException("The workspace was not found.");

    private async Task CheckDestinationAsync(string userId, List<ProjectRecord> projects, CancellationToken cancellationToken, bool requireComplete = false)
    {
        var existing = await database.Projects.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var desired = projects.Select((project, position) => (project, position)).ToDictionary(item => item.project.Id, StringComparer.Ordinal);

        if ((requireComplete && existing.Count != projects.Count) || existing.Any(item =>
            !desired.TryGetValue(item.Id, out var expected) || item.Position != expected.position || item.Project is null || !Equivalent(item.Project, expected.project)))
        {
            throw new InvalidOperationException("Project documents differ from the workspace; no records were overwritten.");
        }
    }

    private static void ValidateProjects(List<ProjectRecord> projects)
    {
        if (projects is null || projects.Any(project => project is null || string.IsNullOrWhiteSpace(project.Id)) ||
            projects.Select(project => project.Id).Distinct(StringComparer.Ordinal).Count() != projects.Count || projects.Count > 1000)
        {
            throw new InvalidOperationException("The workspace contains invalid project IDs or too many projects.");
        }
    }

    private static bool Equivalent<T>(T left, T right) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(left, SharedCommon.JsonOptions), JsonSerializer.SerializeToElement(right, SharedCommon.JsonOptions));
}

/// <summary>Account-scoped project cutover preview tied to a workspace revision.</summary>
public sealed record ProjectCutoverPreview(string UserId, string Revision, int ProjectCount, int ActiveCount);
