using Services.Repositories;
using Shared.Enums;
using Shared.Models;

namespace Services.Workspaces;

/// <summary>Validates project changes and resolves the account's plan.</summary>
public sealed class ProjectWriteService(IProjectWriteRepository projects, IBillingRepository billing, IProjectArchiveRepository? archives = null) : IProjectWriteService
{
    public async Task<ProjectWriteResult> CreateAsync(string userId, ProjectRecord project, CancellationToken cancellationToken = default)
    {
        if (project is null || !ValidId(project.Id) || project.IsArchived || !ValidColour(project.Color) ||
            ProjectName.Validate(project.Name, out var name) != ProjectNameValidation.Valid)
        {
            return new(null, null, ProjectChangeRejection.InvalidInput);
        }

        var clean = new ProjectRecord
        {
            Id = project.Id,
            Name = name,
            Color = project.Color,
            CreatedAt = project.CreatedAt,
            IsArchived = false
        };
        var entitlement = await billing.GetEntitlementAsync(userId, cancellationToken);

        return await projects.CreateAsync(userId, clean, BillingPlanExtensions.ParseOrFree(entitlement.Plan), cancellationToken);
    }

    public Task<ProjectWriteResult> RenameAsync(string userId, string id, string? name, string revision, CancellationToken cancellationToken = default)
    {
        if (!ValidId(id) || string.IsNullOrWhiteSpace(revision) || revision.Length > 512 ||
            ProjectName.Validate(name, out var cleanName) != ProjectNameValidation.Valid)
        {
            return Task.FromResult(new ProjectWriteResult(null, null, ProjectChangeRejection.InvalidInput));
        }

        return projects.RenameAsync(userId, id, cleanName, revision, cancellationToken);
    }

    public async Task<ProjectWriteResult> RestoreAsync(string userId, string id, string revision, CancellationToken cancellationToken = default)
    {
        if (!ValidId(id) || string.IsNullOrWhiteSpace(revision) || revision.Length > 512)
        {
            return new(null, null, ProjectChangeRejection.InvalidInput);
        }

        var entitlement = await billing.GetEntitlementAsync(userId, cancellationToken);

        return await projects.RestoreAsync(userId, id, revision, BillingPlanExtensions.ParseOrFree(entitlement.Plan), cancellationToken);
    }

    public Task<ProjectWriteResult> ArchiveAsync(string userId, string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default)
    {
        if (!ValidId(id) || string.IsNullOrWhiteSpace(revision) || revision.Length > 512 ||
            string.IsNullOrWhiteSpace(workspaceRevision) || workspaceRevision.Length > 512)
        {
            return Task.FromResult(new ProjectWriteResult(null, null, ProjectChangeRejection.InvalidInput));
        }

        return archives is null
            ? Task.FromResult(new ProjectWriteResult(null, null, ProjectChangeRejection.StorageNotReady))
            : archives.ArchiveAsync(userId, id, revision, workspaceRevision, cancellationToken);
    }

    private static bool ValidId(string? id) => id is { Length: > 0 and <= 128 } &&
        !string.IsNullOrWhiteSpace(id) && !id.Any(char.IsControl);

    private static bool ValidColour(string? colour) => colour is { Length: 7 } && colour[0] == '#' &&
        colour.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
}
