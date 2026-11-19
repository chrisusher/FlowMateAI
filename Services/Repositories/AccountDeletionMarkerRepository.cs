using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

// Checkout and deletion coordinate on one ETag-protected, pseudonymous document.
public sealed class AccountDeletionMarkerRepository(DatabaseContext database) : IAccountDeletionMarkerRepository
{
    private static readonly SemaphoreSlim _initialisationLock = new(1, 1);
    private static bool _initialised;

    private static string MarkerId(string userId) => "deleted:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant();

    public async Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var id = MarkerId(userId);
        var marker = await database.Users.AsNoTracking().FirstOrDefaultAsync(user => user.UserId == id && user.Id == id, cancellationToken);

        // Markers written before checkout coordination have no DeletionStarted field.

        return marker is not null && marker.DeletionStarted != false;
    }

    public async Task<bool> MarkAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var id = MarkerId(userId);

        for (var attempt = 0; ; attempt++)
        {
            database.ChangeTracker.Clear();
            var marker = await database.Users.FirstOrDefaultAsync(user => user.UserId == id && user.Id == id, cancellationToken);

            if (marker is null)
            {
                marker = new UserDocument
                {
                    Id = id,
                    UserId = id,
                    DeletionStarted = true
                };
                database.Users.Add(marker);
            }
            else if (marker.DeletionStarted != false)
            {
                return !string.IsNullOrEmpty(marker.CheckoutOperationId) || (marker.ActiveOperationIds?.Count ?? 0) > 0;
            }
            else
            {
                marker.DeletionStarted = true;
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return !string.IsNullOrEmpty(marker.CheckoutOperationId) || (marker.ActiveOperationIds?.Count ?? 0) > 0;
            }
            catch (DbUpdateException) when (attempt < 7)
            {
                // Reload on a conflicting create or ETag update.
            }
        }
    }

    public async Task<bool> TryBeginCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var id = MarkerId(userId);

        for (var attempt = 0; ; attempt++)
        {
            database.ChangeTracker.Clear();
            var marker = await database.Users.FirstOrDefaultAsync(user => user.UserId == id && user.Id == id, cancellationToken);

            if (marker is null)
            {
                marker = new UserDocument
                {
                    Id = id,
                    UserId = id,
                    DeletionStarted = false,
                    CheckoutOperationId = operationId
                };
                database.Users.Add(marker);
            }
            else
            {
                if (marker.DeletionStarted != false || !string.IsNullOrEmpty(marker.CheckoutOperationId))
                {
                    return false;
                }

                marker.CheckoutOperationId = operationId;
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return true;
            }
            catch (DbUpdateException) when (attempt < 7)
            {
            }
        }
    }

    public async Task EndCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var id = MarkerId(userId);

        for (var attempt = 0; ; attempt++)
        {
            database.ChangeTracker.Clear();
            var marker = await database.Users.FirstOrDefaultAsync(user => user.UserId == id && user.Id == id, cancellationToken);

            if (marker is null || marker.CheckoutOperationId != operationId)
            {
                return;
            }

            marker.CheckoutOperationId = null;

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return;
            }
            catch (DbUpdateException) when (attempt < 7)
            {
            }
        }
    }

    public async Task<bool> TryBeginOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var id = MarkerId(userId);

        for (var attempt = 0; ; attempt++)
        {
            database.ChangeTracker.Clear();
            var marker = await database.Users.FirstOrDefaultAsync(user => user.UserId == id && user.Id == id, cancellationToken);

            if (marker is null)
            {
                marker = new UserDocument
                {
                    Id = id,
                    UserId = id,
                    DeletionStarted = false
                };
                database.Users.Add(marker);
            }
            else if (marker.DeletionStarted != false)
            {
                return false;
            }

            if ((marker.ActiveOperationIds?.Count ?? 0) >= 64)
            {
                throw new InvalidOperationException("Too many concurrent account operations.");
            }

            marker.ActiveOperationIds ??= [];
            marker.ActiveOperationIds.Add(operationId);

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return true;
            }
            catch (DbUpdateException) when (attempt < 7)
            {
            }
        }
    }

    public async Task EndOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var id = MarkerId(userId);

        for (var attempt = 0; ; attempt++)
        {
            database.ChangeTracker.Clear();
            var marker = await database.Users.FirstOrDefaultAsync(user => user.UserId == id && user.Id == id, cancellationToken);

            if (marker?.ActiveOperationIds is null || !marker.ActiveOperationIds.Remove(operationId))
            {
                return;
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return;
            }
            catch (DbUpdateException) when (attempt < 7)
            {
            }
        }
    }

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_initialised)
        {
            return;
        }

        await _initialisationLock.WaitAsync(cancellationToken);

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
            _initialisationLock.Release();
        }
    }
}
