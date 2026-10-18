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

        var now = DateTimeOffset.UtcNow;
        var document = await database.Users.FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

        if (document is null)
        {
            var newDocument = new UserDocument
            {
                Id = userId,
                UserId = userId,
                Email = email.Trim(),
                NormalisedEmail = NormaliseEmail(email),
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

        document.Email = email.Trim();
        document.NormalisedEmail = NormaliseEmail(email);
        document.UpdatedAt = now;

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
