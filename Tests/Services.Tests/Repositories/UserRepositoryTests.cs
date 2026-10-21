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
    [Test]
    public async Task RegistrationStoresTrimmedAndNormalisedEmailAndSupportsBothLookups()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = await testDatabase.CreateContextAsync();
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
    public async Task RegistrationSkipsSaveWhenEmailIsUnchanged()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var setupDatabase = await testDatabase.CreateContextAsync();
        await using var database = new CountingContext(testDatabase.CreateOptions());
        var repository = new UserRepository(database);
        await repository.EnsureRegisteredAsync("auth0|user-1", "person@example.test");
        var originalUpdatedAt = (await database.Users.SingleAsync()).UpdatedAt;
        database.SaveChangesCalls = 0;

        await repository.EnsureRegisteredAsync("auth0|user-1", "person@example.test");

        Assert.That(database.SaveChangesCalls, Is.Zero);
        Assert.That((await database.Users.SingleAsync()).UpdatedAt, Is.EqualTo(originalUpdatedAt));
    }

    [Test]
    public async Task RegistrationUpdatesChangedEmail()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var setupDatabase = await testDatabase.CreateContextAsync();
        await using var database = new CountingContext(testDatabase.CreateOptions());
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
    public async Task RegistrationReloadsAndUpdatesWhenAnotherRequestCreatesTheUserFirst()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var setupDatabase = await testDatabase.CreateContextAsync();
        await using var database = new ConflictInjectingContext(testDatabase.CreateOptions(), testDatabase);

        await new UserRepository(database).EnsureRegisteredAsync("auth0|user-1", "requested@example.test");

        var user = await database.Users.AsNoTracking().SingleAsync();
        Assert.That(database.ConflictWasInjected, Is.True);
        Assert.That(user.Email, Is.EqualTo("requested@example.test"));
        Assert.That(user.NormalisedEmail, Is.EqualTo("requested@example.test"));
    }

    [Test]
    public async Task UserDataResetDeletesOnlyTheRequestedUsersRecord()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = await testDatabase.CreateContextAsync();
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
    public void UserDocumentUsesItsOwnContainerAndUserIdPartitionKey()
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
