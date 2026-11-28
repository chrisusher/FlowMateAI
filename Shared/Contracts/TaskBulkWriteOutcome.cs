namespace Shared.Contracts;

public sealed record TaskBulkWriteOutcome(string? Id, TaskWriteResponse? Result, string? ErrorCode, string? ErrorMessage);
