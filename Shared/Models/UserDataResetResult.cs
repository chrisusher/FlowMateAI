namespace Shared.Models;

public sealed record UserDataResetResult(int WorkspaceDocuments, int WorkspaceRecords, int BillingEntitlements, int ArchivedBlobs)
{
    public static UserDataResetResult Empty { get; } = new(0, 0, 0, 0);
}
