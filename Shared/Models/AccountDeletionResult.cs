using Shared.Enums;

namespace Shared.Models;

public sealed record AccountDeletionResult(
    bool Succeeded,
    AccountDeletionStage Stage,
    string Message,
    UserDataResetResult? Data = null);
