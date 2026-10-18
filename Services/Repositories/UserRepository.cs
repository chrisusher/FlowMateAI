using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

public sealed class UserRepository(DatabaseContext database) : IUserRepository
{
    private static readonly SemaphoreSlim InitialisationLock = new(1, 1);
    private static bool _initialised;

    public async Task EnsureRegisteredAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        await EnsureCreatedAsync(cancellationToken);

        var trimmedEmail = email.Trim();
        var normalisedEmail = NormaliseEmail(email);
        var document = await database.Users.FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

        if (document is null)
        {
            var now = DateTimeOffset.UtcNow;
            var newDocument = new UserDocument
            {
                Id = userId,
                UserId = userId,
                Email = trimmedEmail,
                NormalisedEmail = normalisedEmail,
                CreatedAt = now,
                UpdatedAt = now
            };
            database.Users.Add(newDocument);

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return;
            }
            catch (DbUpdateException)
            {
                database.Entry(newDocument).State = EntityState.Detached;
                document = await database.Users.FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

                if (document is null)
                {
                    throw;
                }
            }
        }

        if (string.Equals(document.Email, trimmedEmail, StringComparison.Ordinal)
            && string.Equals(document.NormalisedEmail, normalisedEmail, StringComparison.Ordinal))
        {
            return;
        }

        document.Email = trimmedEmail;
        document.NormalisedEmail = normalisedEmail;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        await EnsureCreatedAsync(cancellationToken);

        var normalizedEmail = NormaliseEmail(email);

        return await database.Users.AsNoTracking()
            .Where(user => user.NormalisedEmail == normalizedEmail)
            .Select(user => user.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> FindEmailByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await EnsureCreatedAsync(cancellationToken);

        return await database.Users.AsNoTracking()
            .Where(user => user.UserId == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_initialised)
        {
            return;
        }

        await InitialisationLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialised)
            {
                return;
            }

            await database.Database.EnsureCreatedAsync(cancellationToken);
            _initialised = true;
        }
        finally
        {
            InitialisationLock.Release();
        }
    }
}
