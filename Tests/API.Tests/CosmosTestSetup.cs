using Services.Tests;

namespace API.Tests;

[SetUpFixture]
public sealed class CosmosTestSetup
{
    [OneTimeSetUp]
    public Task StartCosmosEmulatorAsync() => CosmosTestEnvironment.StartAsync();

    [OneTimeTearDown]
    public Task StopCosmosEmulatorAsync() => CosmosTestEnvironment.DisposeAsync().AsTask();
}
