using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class UserRepositoryTests
{
    private CosmosTestDatabase _testDatabase = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        if (_testDatabase is not null)
        {
            await _testDatabase.DisposeAsync();
        }
    }

    [Test]
    public async Task DeleteUserRecordsAsync_MarkedUser_MarkerSurvivesWithoutSubject()
    {
        await using var database = _testDatabase.CreateContext();
        var markers = new AccountDeletionMarkerRepository(database);
        await markers.MarkAsync("auth0|user-1");
        await markers.MarkAsync("auth0|user-1");

        var reset = new UserDataResetRepository(database, new ConfigurationBuilder().Build());
        await reset.DeleteUserRecordsAsync("auth0|user-1");

        Assert.That(await new AccountDeletionMarkerRepository(database).IsMarkedAsync("auth0|user-1"), Is.True);
        Assert.That(await markers.IsMarkedAsync("auth0|user-2"), Is.False);
        var marker = await database.Users.AsNoTracking().SingleAsync();
        Assert.That(marker.UserId, Does.Not.Contain("auth0|user-1"));
        Assert.That(marker.Email, Is.Empty);
    }

    [Test]
    public async Task TryBeginCheckoutAsync_DeletionMarked_RejectsCheckoutUntilOperationsFinish()
    {
        await using var database = _testDatabase.CreateContext();
        var markers = new AccountDeletionMarkerRepository(database);

        Assert.That(await markers.TryBeginCheckoutAsync("auth0|user-1", "operation-1"), Is.True);
        Assert.That(await markers.IsMarkedAsync("auth0|user-1"), Is.False);
        Assert.That(await markers.MarkAsync("auth0|user-1"), Is.True);
        Assert.That(await markers.TryBeginCheckoutAsync("auth0|user-1", "operation-2"), Is.False);
        await markers.EndCheckoutAsync("auth0|user-1", "operation-1");
        Assert.That(await markers.MarkAsync("auth0|user-1"), Is.False);
        Assert.That(await markers.IsMarkedAsync("auth0|user-1"), Is.True);
    }

    [Test]
    public async Task MarkAsync_ActiveOperations_WaitsAndRejectsNewOperations()
    {
        await using var database = _testDatabase.CreateContext();
        var markers = new AccountDeletionMarkerRepository(database);

        Assert.That(await markers.TryBeginOperationAsync("auth0|user-1", "write-1"), Is.True);
        Assert.That(await markers.TryBeginOperationAsync("auth0|user-1", "write-2"), Is.True);
        Assert.That(await markers.MarkAsync("auth0|user-1"), Is.True);
        Assert.That(await markers.TryBeginOperationAsync("auth0|user-1", "write-3"), Is.False);
        await markers.EndOperationAsync("auth0|user-1", "write-1");
        Assert.That(await markers.MarkAsync("auth0|user-1"), Is.True);
        await markers.EndOperationAsync("auth0|user-1", "write-2");
        Assert.That(await markers.MarkAsync("auth0|user-1"), Is.False);
    }

    [Test]
    public async Task EnsureRegisteredAsync_UntrimmedEmail_StoresNormalisedEmailAndSupportsLookups()
    {
        await using var database = _testDatabase.CreateContext();
        var repository = new UserRepository(database);

        await repository.EnsureRegisteredAsync("auth0|user-1", "  Person@Example.Test  ");

        var user = await database.Users.AsNoTracking().SingleAsync();
        Assert.That(user.Email, Is.EqualTo("Person@Example.Test"));
        Assert.That(user.NormalisedEmail, Is.EqualTo("person@example.test"));
        Assert.That(await repository.FindUserIdByEmailAsync(" PERSON@example.test "), Is.EqualTo("auth0|user-1"));
        Assert.That(await repository.FindEmailByUserIdAsync("auth0|user-1"), Is.EqualTo("Person@Example.Test"));
        Assert.That(await repository.FindEmailByUserIdAsync("auth0|missing"), Is.Null);
    }

    [Test]
    public async Task EnsureRegisteredAsync_UnchangedEmail_DoesNotSave()
    {
        await using var database = new CountingContext(_testDatabase.CreateOptions());
        var repository = new UserRepository(database);
        await repository.EnsureRegisteredAsync("auth0|user-1", "person@example.test");
        var originalUpdatedAt = (await database.Users.SingleAsync()).UpdatedAt;
        database.SaveChangesCalls = 0;

        await repository.EnsureRegisteredAsync("auth0|user-1", "person@example.test");

        Assert.That(database.SaveChangesCalls, Is.Zero);
        Assert.That((await database.Users.SingleAsync()).UpdatedAt, Is.EqualTo(originalUpdatedAt));
    }

    [Test]
    public async Task EnsureRegisteredAsync_ChangedEmail_UpdatesUser()
    {
        await using var database = new CountingContext(_testDatabase.CreateOptions());
        var repository = new UserRepository(database);
        await repository.EnsureRegisteredAsync("auth0|user-1", "old@example.test");
        var originalUpdatedAt = (await database.Users.SingleAsync()).UpdatedAt;
        database.SaveChangesCalls = 0;

        await repository.EnsureRegisteredAsync("auth0|user-1", "New@Example.Test");

        var updatedUser = await database.Users.AsNoTracking().SingleAsync();
        Assert.That(database.SaveChangesCalls, Is.EqualTo(1));
        Assert.That(updatedUser.Email, Is.EqualTo("New@Example.Test"));
        Assert.That(updatedUser.NormalisedEmail, Is.EqualTo("new@example.test"));
        Assert.That(updatedUser.UpdatedAt, Is.GreaterThanOrEqualTo(originalUpdatedAt));
    }

    [Test]
    public async Task EnsureRegisteredAsync_ConcurrentRegistration_ReloadsAndUpdatesUser()
    {
        await using var database = new ConflictInjectingContext(_testDatabase.CreateOptions(), _testDatabase);

        await new UserRepository(database).EnsureRegisteredAsync("auth0|user-1", "requested@example.test");

        var user = await database.Users.AsNoTracking().SingleAsync();
        Assert.That(database.ConflictWasInjected, Is.True);
        Assert.That(user.Email, Is.EqualTo("requested@example.test"));
        Assert.That(user.NormalisedEmail, Is.EqualTo("requested@example.test"));
    }

    [Test]
    public async Task DeleteUserRecordsAsync_RequestedUser_DeletesOnlyTheirRecord()
    {
        await using var database = _testDatabase.CreateContext();
        database.Users.AddRange(
            new UserDocument
            {
                Id = "user-1",
                UserId = "user-1",
                Email = "one@example.test"
            },
            new UserDocument
            {
                Id = "user-2",
                UserId = "user-2",
                Email = "two@example.test"
            });

        await database.SaveChangesAsync();
        var repository = new UserDataResetRepository(database, new ConfigurationBuilder().Build());

        var deleted = await repository.DeleteUserRecordsAsync("user-1");

        Assert.That(deleted, Is.EqualTo(1));
        Assert.That(await database.Users.AsNoTracking().Select(user => user.UserId).ToListAsync(), Is.EqualTo(new[]
        {
            "user-2"
        }));
    }

    [Test]
    public void UserDocument_Model_UsesUsersContainerAndUserIdPartitionKey()
    {
        using var database = new CosmosModelContext();
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(UserDocument))!;

        Assert.That(entity.GetContainer(), Is.EqualTo("Users"));
        Assert.That(entity.FindPrimaryKey()!.Properties.Select(property => property.Name), Is.EqualTo(new[]
        {
            nameof(UserDocument.Id)
        }));

        Assert.That(entity.GetPartitionKeyPropertyNames(), Is.EqualTo(new[]
        {
            nameof(UserDocument.UserId)
        }));
        Assert.That(entity.FindProperty(nameof(UserDocument.ETag))!.IsConcurrencyToken, Is.True);
    }

    private sealed class CountingContext(DbContextOptions<DatabaseContext> options) : DatabaseContext(options)
    {
        public int SaveChangesCalls
        {
            get; set;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class ConflictInjectingContext(DbContextOptions<DatabaseContext> options, CosmosTestDatabase testDatabase)
        : DatabaseContext(options)
    {
        public bool ConflictWasInjected
        {
            get; private set;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!ConflictWasInjected)
            {
                ConflictWasInjected = true;

                await using var competingRequest = testDatabase.CreateContext();

                competingRequest.Users.Add(new UserDocument
                {
                    Id = "auth0|user-1",
                    UserId = "auth0|user-1",
                    Email = "initial@example.test",
                    NormalisedEmail = "initial@example.test"
                });
                await competingRequest.SaveChangesAsync(cancellationToken);

                throw new DbUpdateException("A concurrent request created the same user.");
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CosmosModelContext() : DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
        .UseCosmos("https://localhost:8081", Convert.ToBase64String(new byte[64]), "tests")
        .Options);
}
