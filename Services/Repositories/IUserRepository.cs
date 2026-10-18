namespace Services.Repositories;

public interface IUserRepository
{
    Task EnsureRegisteredAsync(string userId, string email, CancellationToken cancellationToken = default);

    Task<string?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<string?> FindEmailByUserIdAsync(string userId, CancellationToken cancellationToken = default);
}
