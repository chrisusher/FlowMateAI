using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

public sealed class UserRepository(DatabaseContext database) : IUserRepository
{
    public async Task EnsureRegisteredAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var now = DateTimeOffset.UtcNow;
        var document = await database.Users.FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

        if (document is null)
        {
            database.Users.Add(new UserDocument
            {
                Id = userId,
                UserId = userId,
                Email = email.Trim(),
                NormalisedEmail = NormaliseEmail(email),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            document.Email = email.Trim();
            document.NormalisedEmail = NormaliseEmail(email);
            document.UpdatedAt = now;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var normalizedEmail = NormaliseEmail(email);

        return await database.Users.AsNoTracking()
            .Where(user => user.NormalisedEmail == normalizedEmail)
            .Select(user => user.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();
}
