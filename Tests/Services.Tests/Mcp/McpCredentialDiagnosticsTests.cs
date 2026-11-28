using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Services.Database;
using Services.Mcp;
using Services.Repositories;

namespace Services.Tests.Mcp;

[TestFixture]
public sealed class McpCredentialDiagnosticsTests
{
    [Test]
    public async Task CreateAsync_VaultWriteDenied_LogsStatusWithoutSecretAndDoesNotPersist()
    {
        var repository = new StubRepository();
        var logger = new CaptureLogger();
        var service = new McpCredentialService(repository, logger, new DeniedSecretClient());

        await Assert.ThrowsAsync<RequestFailedException>(() => service.CreateAsync("private-user", "name", 90));
        Assert.That(repository.Created, Is.False);
        Assert.That(logger.Message, Does.Contain("set").And.Contain("403"));
        Assert.That(logger.Message, Does.Not.Contain("private-user").And.Not.Contain("sensitive payload"));
    }

    private sealed class DeniedSecretClient : SecretClient
    {
        public override Task<Response<KeyVaultSecret>> SetSecretAsync(string name, string value, CancellationToken cancellationToken = default) =>
            throw new RequestFailedException(403, "sensitive payload", "Forbidden", null);
    }

    private sealed class CaptureLogger : ILogger<McpCredentialService>
    {
        public string Message { get; private set; } = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Message = formatter(state, exception);
    }

    private sealed class StubRepository : IMcpCredentialRepository
    {
        public bool Created { get; private set; }
        public Task CreateAsync(McpKeyDocument document, CancellationToken cancellationToken = default)
        {
            Created = true;

            return Task.CompletedTask;
        }
        public Task DeleteAsync(string userId, string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<McpKeyDocument>> ListAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<McpKeyDocument?> GetAsync(string userId, string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<McpKeyDocument?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ReplaceAsync(McpKeyDocument document, string? etag, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
