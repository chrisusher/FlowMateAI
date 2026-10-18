using Services.Repositories;

namespace Services.Users;

public sealed class UserService(IUserRepository repository) : IUserService
{
    public Task EnsureRegisteredAsync(string userId, string email, CancellationToken cancellationToken = default) =>
        repository.EnsureRegisteredAsync(userId, email, cancellationToken);

    public Task<string?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        repository.FindUserIdByEmailAsync(email, cancellationToken);
}
