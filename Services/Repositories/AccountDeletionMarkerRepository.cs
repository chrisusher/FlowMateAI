using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

// Markers share the existing Users container but contain no email or raw subject.
public sealed class AccountDeletionMarkerRepository(DatabaseContext database) : IAccountDeletionMarkerRepository
{
    private static readonly SemaphoreSlim InitialisationLock = new(1, 1);
    private static bool _initialised;
    private static string MarkerId(string userId) => "deleted:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant();

    public async Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!_initialised)
        {
            await InitialisationLock.WaitAsync(cancellationToken);

            try
            {
                if (!_initialised)
                {
                    await database.Database.EnsureCreatedAsync(cancellationToken);
                    _initialised = true;
                }
            }
            finally
            {
                InitialisationLock.Release();
            }
        }

        var id = MarkerId(userId);

        return await database.Users.AsNoTracking().AnyAsync(user => user.UserId == id && user.Id == id, cancellationToken);
    }

    public async Task MarkAsync(string userId, CancellationToken cancellationToken = default)
    {
        var id = MarkerId(userId);

        if (await IsMarkedAsync(userId, cancellationToken))
        {
            return;
        }

        database.Users.Add(new UserDocument { Id = id, UserId = id });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            if (!await IsMarkedAsync(userId, cancellationToken))
            {
                throw;
            }
        }
    }
}
