namespace Services.Coach;

public interface IFocusCoachService
{
    Task<FocusCoachAnswer> AskAsync(string userId, string prompt, IReadOnlyList<Shared.Models.ChatMessageRecord> history, CancellationToken cancellationToken = default);
}
