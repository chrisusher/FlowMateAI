using System.Security.Cryptography;
using System.Text.Json;
using ChrisUsher.Core.Shared;
using Shared.Models;

namespace Shared.Contracts;

public sealed record WorkspaceCollectionManifest(
    WorkspaceSnapshot Workspace,
    string Revision,
    string ProjectsHash,
    string TasksHash,
    string SessionsHash,
    string ConversationsHash)
{
    public static string Hash<T>(IReadOnlyList<T> records) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(records, SharedCommon.JsonOptions)));

    public static WorkspaceCollectionManifest Create(WorkspaceResponse response)
    {
        var source = response.Workspace;
        var metadata = new WorkspaceSnapshot
        {
            DisplayName = source.DisplayName,
            TimeZone = source.TimeZone,
            Plan = source.Plan,
            ClientId = source.ClientId,
            Muted = source.Muted,
            FirstRunState = source.FirstRunState,
            Timer = source.Timer
        };

        return new(metadata, response.Revision, Hash(source.Projects), Hash(source.Tasks), Hash(source.Sessions), Hash(source.Conversations));
    }
}
