namespace Shared.Contracts;

public sealed record TaskBulkWriteRequest(List<TaskCreateRequest>? Changes);
