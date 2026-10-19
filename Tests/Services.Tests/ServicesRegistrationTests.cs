using Services.Repositories;
using Services.Workspaces;

namespace Services.Tests;

[TestFixture]
public sealed class ServicesRegistrationTests
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:AccountEndpoint"] = "https://localhost:8081/",
                ["Database:Key"] = Convert.ToBase64String(new byte[64]),
                ["Database:DatabaseName"] = "flowmate-tests"
            })
            .Build();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddServices(configuration);

        _provider = serviceCollection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
        _scope = _provider.CreateScope();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Test]
    public void AddServicesWithoutStorageBuildsAndResolvesWorkspaceService()
    {
        var archiveRepository = _scope.ServiceProvider.GetRequiredService<IActivityArchiveRepository>();
        var workspaceService = _scope.ServiceProvider.GetRequiredService<IWorkspaceService>();

        Assert.That(archiveRepository, Is.InstanceOf<DisabledActivityArchiveRepository>());
        Assert.That(workspaceService, Is.InstanceOf<WorkspaceService>());
    }
}
