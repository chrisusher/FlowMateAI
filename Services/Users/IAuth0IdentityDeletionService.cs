namespace Services.Users;

public interface IAuth0IdentityDeletionService
{
    bool IsConfigured { get; }

    Task DeleteAsync(string userId, CancellationToken cancellationToken = default);
}
