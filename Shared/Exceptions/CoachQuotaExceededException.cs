namespace Shared.Exceptions;

public sealed class CoachQuotaExceededException(int used, int limit) : Exception("You've used all your monthly coach prompts.")
{
    public int Used { get; } = used;
    public int Limit { get; } = limit;
}
