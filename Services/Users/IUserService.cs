namespace Services.Users;

public interface IUserService
{
    Task EnsureRegisteredAsync(string userId, string email, CancellationToken cancellationToken = default);
    Task<string?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default);
}
