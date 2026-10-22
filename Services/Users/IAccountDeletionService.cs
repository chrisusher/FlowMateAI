using Shared.Models;

namespace Services.Users;

public interface IAccountDeletionService
{
    Task<AccountDeletionResult> DeleteAsync(string userId, CancellationToken cancellationToken = default);
}
