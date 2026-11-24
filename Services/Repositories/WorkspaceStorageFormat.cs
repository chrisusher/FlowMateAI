using System.Text.Json;
using ChrisUsher.Core.Shared;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

/// <summary>Reads legacy workspaces and prepares historical focus repairs.</summary>
public static class WorkspaceStorageFormat
{
    /// <summary>Reads a structured workspace or an existing legacy payload.</summary>
    public static WorkspaceSnapshot Read(WorkspaceDocument document) => document.Workspace ??
        (string.IsNullOrEmpty(document.Payload)
            ? throw new InvalidDataException("Workspace document has no workspace data.")
            : JsonSerializer.Deserialize<WorkspaceSnapshot>(document.Payload, SharedCommon.JsonOptions) ?? throw new InvalidDataException("Workspace payload is empty."));

    /// <summary>Adds a known planned duration to records where it was not captured.</summary>
    public static int BackfillPlannedSeconds(WorkspaceSnapshot workspace, int plannedSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plannedSeconds);
        var changed = 0;

        foreach (var session in workspace.Sessions.Where(session => session.PlannedSeconds is null))
        {
            session.PlannedSeconds = plannedSeconds;
            changed++;
        }

        return changed;
    }
}
