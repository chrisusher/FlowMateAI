using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using API.Functions.V1;
using Services.Users;
using Services.Repositories;
using API.Functions.V1.Projects;
using Azure.Core.Serialization;
using ChrisUsher.Core.Shared;
using Services.ApiRequests;
using Services.Workspaces;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;
using Services.Database;
using Microsoft.EntityFrameworkCore;
using API.Functions.V1.Projects.Archive;
using API.Functions.V1.Projects.Restore;
using Services.Tests;

namespace API.Tests;

[TestFixture]
public sealed class Auth0TokenValidatorTests
{
    private const string AUTHORITY = "https://flowmate-test.eu.auth0.com/";
    private const string AUDIENCE = "https://api.flowmate.test";

    [Test]
    public void EveryApplicationFunctionExceptHealthAndStripeWebhookInjectsTokenValidator()
    {
        var functionTypes = typeof(Auth0TokenValidator).Assembly.GetTypes()
            .Where(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Any(method => method.GetCustomAttribute<FunctionAttribute>() is not null))
            .ToArray();

        var allowedPublicFunctions = new HashSet<string>(StringComparer.Ordinal)
        {
            "HealthCheck",
            "StripeWebhook"
        };

        var unprotectedFunctions = functionTypes
            .Where(type => !allowedPublicFunctions.Contains(type.Name))
            .Where(type => !type.GetConstructors().Any(constructor => constructor.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(Auth0TokenValidator))))
            .Select(type => type.Name);

        Assert.That(unprotectedFunctions, Is.Empty);
        Assert.That(functionTypes.Select(type => type.Name).Where(allowedPublicFunctions.Contains).ToHashSet(),
            Is.EquivalentTo(allowedPublicFunctions));
    }

    [Test]
    public async Task ValidateAsync_AcceptsSignedTokenWithConfiguredIssuerAudienceAndSubject()
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key);
        var token = CreateToken(key, subject: "auth0|customer-1");

        var principal = await validator.ValidateAsync($"Bearer {token}");

        Assert.That(principal?.FindFirst("sub")?.Value, Is.EqualTo("auth0|customer-1"));
    }

    [Test]
    public async Task DeletedSubjectCannotUseTokenButCanRetryDeletion()
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key, markers: new MarkedAccounts());
        var token = $"Bearer {CreateToken(key, subject: "auth0|customer-1")}";
        Assert.That(await validator.ValidateAsync(token), Is.Null);
        Assert.That(await validator.ValidateForDeletionAsync(token), Is.Not.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task ValidateAsync_RejectsEmptySubject(string subject)
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key);
        var token = CreateToken(key, subject);

        Assert.That(await validator.ValidateAsync($"Bearer {token}"), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_RejectsWrongAudienceAndMissingExpiry()
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key);

        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key, audience: "another-api")}"), Is.Null);
        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key, includeExpiry: false)}"), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_RejectsWrongIssuerExpiredAndNotYetValidTokens()
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key);

        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key, issuer: "https://attacker.example/")}"), Is.Null);
        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds())}"), Is.Null);
        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key, notBefore: DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds())}"), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_RejectsUnknownSigningKeyAndInvalidSignature()
    {
        using var key = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        var validator = CreateValidator(key);

        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key, keyId: "unknown-key")}"), Is.Null);
        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key, signingKey: otherKey)}"), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_FailsClosedWhenAuth0ConfigurationIsMissing()
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key, includeConfiguration: false);

        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key)}"), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_RejectsMalformedOrMissingBearerToken()
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key);

        Assert.That(await validator.ValidateAsync(null), Is.Null);
        Assert.That(await validator.ValidateAsync("Basic credentials"), Is.Null);
        Assert.That(await validator.ValidateAsync("Bearer not-a-jwt"), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_FailsClosedWhenJwksEndpointIsUnavailable()
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key, HttpStatusCode.ServiceUnavailable);

        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key)}"), Is.Null);
    }

    [Test]
    public async Task ValidateAsync_FailsClosedWhenJwksRequestTimesOut()
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key, timeout: true);

        Assert.That(await validator.ValidateAsync($"Bearer {CreateToken(key)}"), Is.Null);
    }

    [Test]
    public async Task GetEmailAsyncReturnsEmailForTheMatchingProfileSubject()
    {
        using var key = RSA.Create(2048);
        var handler = CreateHandler(key, """{"sub":"auth0|customer-1","email":"person@example.test"}""");
        var validator = CreateValidator(key, handler: handler);

        var email = await validator.GetEmailAsync("Bearer test-token", "auth0|customer-1");

        Assert.That(email, Is.EqualTo("person@example.test"));
        Assert.That(handler.UserInfoRequests, Is.EqualTo(1));
        Assert.That(handler.UserInfoAuthorization, Is.EqualTo("Bearer test-token"));
    }

    [TestCase("""{"sub":"auth0|someone-else","email":"person@example.test"}""")]
    [TestCase("""{"sub":"auth0|customer-1"}""")]
    public async Task GetEmailAsyncRejectsProfileWithoutMatchingSubjectAndEmail(string profileJson)
    {
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key, handler: CreateHandler(key, profileJson));

        Assert.That(await validator.GetEmailAsync("Bearer test-token", "auth0|customer-1"), Is.Null);
    }

    [Test]
    public async Task UserAsyncUsesStoredEmailWithoutCallingUserInfoAgain()
    {
        using var key = RSA.Create(2048);
        var handler = CreateHandler(key);
        var users = new TestUserService("person@example.test");

        var result = await HttpFunction.UserAsync(
            new TestHttpRequestData($"Bearer {CreateToken(key)}"),
            CreateValidator(key, handler: handler),
            users,
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(("auth0|customer-1", "person@example.test")));
        Assert.That(users.EmailLookupCalls, Is.EqualTo(1));
        Assert.That(users.RegistrationCalls, Is.Zero);
        Assert.That(handler.UserInfoRequests, Is.Zero);
    }

    [Test]
    public async Task UserAsyncFetchesAndRegistersEmailForANewUserOnlyOnce()
    {
        using var key = RSA.Create(2048);
        var handler = CreateHandler(key, """{"sub":"auth0|customer-1","email":"person@example.test"}""");
        var users = new TestUserService(null);

        var result = await HttpFunction.UserAsync(
            new TestHttpRequestData($"Bearer {CreateToken(key)}"),
            CreateValidator(key, handler: handler),
            users,
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(("auth0|customer-1", "person@example.test")));
        Assert.That(users.EmailLookupCalls, Is.EqualTo(1));
        Assert.That(users.RegistrationCalls, Is.EqualTo(1));
        Assert.That(users.RegisteredEmail, Is.EqualTo("person@example.test"));
        Assert.That(handler.UserInfoRequests, Is.EqualTo(1));
    }

    [Test]
    public async Task AuthenticatedRequestRemainsAdmittedUntilRequestCompletion()
    {
        using var key = RSA.Create(2048);
        var markers = new TrackingOperations();
        var request = new TestHttpRequestData($"Bearer {CreateToken(key)}", markers);

        var result = await HttpFunction.UserAsync(request, CreateValidator(key), new TestUserService("person@example.test"), CancellationToken.None);

        Assert.That(result.UserId, Is.EqualTo("auth0|customer-1"));
        Assert.That(await markers.MarkAsync(result.UserId!), Is.True);
        Assert.That(await markers.TryBeginOperationAsync(result.UserId!, "late-request"), Is.False);
        await AccountOperation.CompleteAsync(request.FunctionContext);
        Assert.That(await markers.MarkAsync(result.UserId!), Is.False);
    }

    [Test]
    public async Task Run_AuthenticatedCreate_ReturnsReadableProjectResponse()
    {
        using var key = RSA.Create(2048);
        var projects = new TestProjects();
        var function = new CreateProject(CreateValidator(key), new TestUserService("person@example.test"), new AllowRequests(), projects);
        var input = new ProjectCreateRequest("project", "Project", "#112233", DateTimeOffset.UtcNow);
        var request = new WritableRequest($"Bearer {CreateToken(key)}", JsonSerializer.Serialize(input, SharedCommon.JsonOptions));

        var response = await function.Run(request, CancellationToken.None);
        response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<ProjectWriteResponse>(response.Body, SharedCommon.JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(projects.Owner, Is.EqualTo("auth0|customer-1"));
        Assert.That(result!.Project.Id, Is.EqualTo("project"));
        Assert.That(result.Revision, Is.EqualTo("revision"));
        await AccountOperation.CompleteAsync(request.FunctionContext);
    }

    [Test]
    public async Task Run_MissingAuthentication_RejectsProjectWrite()
    {
        using var key = RSA.Create(2048);
        var projects = new TestProjects();
        var function = new CreateProject(CreateValidator(key), new TestUserService("person@example.test"), new AllowRequests(), projects);
        var request = new WritableRequest("", "{}");

        var response = await function.Run(request, CancellationToken.None);
        response.Body.Position = 0;
        var error = await JsonSerializer.DeserializeAsync<ApiError>(response.Body, SharedCommon.JsonOptions);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(error!.Code, Is.EqualTo("unauthorized"));
        Assert.That(projects.Owner, Is.Null);
    }

    [Test]
    public async Task Run_AuthenticatedProjectLifecycle_PersistsChangesAndIsolatesAccounts()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
        const string userId = "auth0|customer-1";
        var workspaces = new WorkspaceRepository(database, new WorkspaceCollectionSynchronizer(database));
        await workspaces.SaveAsync(userId, new WorkspaceSnapshot(), null);
        var billing = new TestBilling();
        var service = new ProjectWriteService(new ProjectWriteRepository(database), billing, new ProjectArchiveRepository(database));
        using var key = RSA.Create(2048);
        var validator = CreateValidator(key);
        var users = new TestUserService("person@example.test");
        var limiter = new AllowRequests();
        var authorization = $"Bearer {CreateToken(key)}";

        var create = new CreateProject(validator, users, limiter, service);
        var createRequest = new WritableRequest(authorization, JsonSerializer.Serialize(new ProjectCreateRequest("project", "Client work", "#112233", DateTimeOffset.UtcNow), SharedCommon.JsonOptions));
        var created = await create.Run(createRequest, CancellationToken.None);
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createdBody = await ReadResponseAsync<ProjectWriteResponse>(created);
        await AccountOperation.CompleteAsync(createRequest.FunctionContext);

        var rename = new RenameProject(validator, users, limiter, service);
        var renameRequest = new WritableRequest(authorization, JsonSerializer.Serialize(new ProjectRenameRequest("Renamed", createdBody.Revision), SharedCommon.JsonOptions));
        var renamed = await rename.Run(renameRequest, "project", CancellationToken.None);
        Assert.That(renamed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var renamedBody = await ReadResponseAsync<ProjectWriteResponse>(renamed);
        Assert.That(renamedBody.Project.Name, Is.EqualTo("Renamed"));
        await AccountOperation.CompleteAsync(renameRequest.FunctionContext);

        var staleRequest = new WritableRequest(authorization, JsonSerializer.Serialize(new ProjectRenameRequest("Stale", createdBody.Revision), SharedCommon.JsonOptions));
        var stale = await rename.Run(staleRequest, "project", CancellationToken.None);
        Assert.That(stale.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await AccountOperation.CompleteAsync(staleRequest.FunctionContext);

        var currentWorkspace = await database.Workspaces.AsNoTracking().SingleAsync(item => item.UserId == userId && item.Id == "workspace");
        var archive = new ArchiveProject(validator, users, limiter, service);
        var archiveRequest = new WritableRequest(authorization, JsonSerializer.Serialize(new ProjectArchiveRequest(renamedBody.Revision, currentWorkspace.ETag), SharedCommon.JsonOptions));
        var archived = await archive.Run(archiveRequest, "project", CancellationToken.None);
        Assert.That(archived.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var archivedBody = await ReadResponseAsync<ProjectWriteResponse>(archived);
        Assert.That(archivedBody.Project.IsArchived, Is.True);
        Assert.That(archivedBody.WorkspaceRevision, Is.Not.Empty);
        Assert.That(archivedBody.Timer, Is.Not.Null);
        await AccountOperation.CompleteAsync(archiveRequest.FunctionContext);

        var restore = new RestoreProject(validator, users, limiter, service);
        var restoreRequest = new WritableRequest(authorization, JsonSerializer.Serialize(new ProjectStateRequest(archivedBody.Revision), SharedCommon.JsonOptions));
        var restored = await restore.Run(restoreRequest, "project", CancellationToken.None);
        Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await ReadResponseAsync<ProjectWriteResponse>(restored)).Project.IsArchived, Is.False);
        await AccountOperation.CompleteAsync(restoreRequest.FunctionContext);

        database.ChangeTracker.Clear();
        var reloaded = await workspaces.GetAsync(userId);
        Assert.That(reloaded!.Workspace!.Projects.Single().Name, Is.EqualTo("Renamed"));
        Assert.That(reloaded.Workspace.Projects.Single().IsArchived, Is.False);
        Assert.That(await new WorkspaceItemsRepository(database).GetProjectAsync("other-account", "project"), Is.Null);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseData response)
    {
        response.Body.Position = 0;

        return (await JsonSerializer.DeserializeAsync<T>(response.Body, SharedCommon.JsonOptions))!;
    }

    private static Auth0TokenValidator CreateValidator(
        RSA key,
        HttpStatusCode status = HttpStatusCode.OK,
        bool includeConfiguration = true,
        bool timeout = false,
        TestHttpMessageHandler? handler = null,
        IAccountDeletionMarkerRepository? markers = null)
    {
        var settings = new Dictionary<string, string?>();

        if (includeConfiguration)
        {
            settings["Auth0:Authority"] = AUTHORITY;
            settings["Auth0:Audience"] = AUDIENCE;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var publicKey = key.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kid = "test-key",
                    kty = "RSA",
                    n = Base64Url(publicKey.Modulus!),
                    e = Base64Url(publicKey.Exponent!)
                }
            }
        });
        var clients = new TestHttpClientFactory(handler ?? new TestHttpMessageHandler(status, jwks, timeout));

        var provider = new ServiceCollection()
            .AddSingleton(markers ?? new UnmarkedAccounts())
            .BuildServiceProvider();

        return new Auth0TokenValidator(configuration, clients, provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static TestHttpMessageHandler CreateHandler(RSA key, string? profileJson = null)
    {
        var publicKey = key.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kid = "test-key",
                    kty = "RSA",
                    n = Base64Url(publicKey.Modulus!),
                    e = Base64Url(publicKey.Exponent!)
                }
            }
        });

        return new TestHttpMessageHandler(HttpStatusCode.OK, jwks, false,
            profileJson ?? """{"sub":"auth0|customer-1","email":"person@example.test"}""");
    }

    private static string CreateToken(
        RSA key,
        string subject = "auth0|customer-1",
        string audience = AUDIENCE,
        bool includeExpiry = true,
        string issuer = AUTHORITY,
        string keyId = "test-key",
        long? expiresAt = null,
        long? notBefore = null,
        RSA? signingKey = null)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            alg = "RS256",
            kid = keyId
        })));
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = issuer,
            ["aud"] = audience,
            ["sub"] = subject
        };

        if (includeExpiry)
        {
            payload["exp"] = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        }

        if (notBefore.HasValue)
        {
            payload["nbf"] = notBefore.Value;
        }

        var body = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        var unsignedToken = $"{header}.{body}";
        var signature = (signingKey ?? key).SignData(Encoding.ASCII.GetBytes(unsignedToken), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{unsignedToken}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TestHttpMessageHandler(HttpStatusCode status, string jwks, bool timeout, string profileJson = "") : HttpMessageHandler
    {
        public int UserInfoRequests
        {
            get; private set;
        }
        public string? UserInfoAuthorization
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (timeout)
            {
                return Task.FromException<HttpResponseMessage>(new TaskCanceledException("JWKS request timed out."));
            }

            if (request.RequestUri?.AbsolutePath == "/userinfo")
            {
                UserInfoRequests++;
                UserInfoAuthorization = request.Headers.Authorization?.ToString();

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(profileJson, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(jwks, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestUserService(string? storedEmail) : IUserService
    {
        public int EmailLookupCalls
        {
            get; private set;
        }
        public int RegistrationCalls
        {
            get; private set;
        }
        public string? RegisteredEmail
        {
            get; private set;
        }

        public Task EnsureRegisteredAsync(string userId, string email, CancellationToken cancellationToken = default)
        {
            RegistrationCalls++;
            RegisteredEmail = email;

            return Task.CompletedTask;
        }

        public Task<string?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> FindEmailByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            EmailLookupCalls++;

            return Task.FromResult(storedEmail);
        }
    }

    private sealed class TestHttpRequestData : HttpRequestData
    {
        public TestHttpRequestData(string authorization, IAccountDeletionMarkerRepository? markers = null)
            : base(new TestFunctionContext(markers ?? new UnmarkedAccounts()))
        {
            Headers.Add("Authorization", authorization);
        }

        public override Stream Body => Stream.Null;
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
        public override Uri Url => new("https://api.flowmate.test/test");
        public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();
        public override string Method => "GET";
        public override HttpResponseData CreateResponse() => throw new NotSupportedException();
    }

    private sealed class WritableRequest : HttpRequestData
    {
        public WritableRequest(string authorization, string body) : base(new TestFunctionContext(new UnmarkedAccounts()))
        {
            if (!string.IsNullOrEmpty(authorization))
            {
                Headers.Add("Authorization", authorization);
            }
            Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        }

        public override Stream Body { get; }
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
        public override Uri Url => new("https://api.flowmate.test/v1/projects");
        public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();
        public override string Method => "POST";
        public override HttpResponseData CreateResponse() => new WritableResponse(FunctionContext);
    }

    private sealed class WritableResponse(FunctionContext context) : HttpResponseData(context)
    {
        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new();
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpCookies Cookies => null!;
    }

    private sealed class AllowRequests : IAuthenticatedApiRateLimiter
    {
        public Task<ApiRateLimitAdmission> AdmitAsync(string userId, AuthenticatedApiRateLimitScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApiRateLimitAdmission(true, 0));
    }

    private sealed class TestProjects : IProjectWriteService
    {
        public string? Owner { get; private set; }

        public Task<ProjectWriteResult> CreateAsync(string userId, ProjectRecord project, CancellationToken cancellationToken = default)
        {
            Owner = userId;

            return Task.FromResult(new ProjectWriteResult(project, "revision", ProjectChangeRejection.None));
        }

        public Task<ProjectWriteResult> RenameAsync(string userId, string id, string? name, string revision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProjectWriteResult> RestoreAsync(string userId, string id, string revision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProjectWriteResult> ArchiveAsync(string userId, string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestBilling : IBillingRepository
    {
        public Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingEntitlementDocument
            {
                UserId = userId
            });
        public Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateExistingEntitlementAsync(string userId, Action<BillingEntitlementDocument> update, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestFunctionContext(IAccountDeletionMarkerRepository markers) : FunctionContext
    {
        public override string InvocationId => "test-invocation";
        public override string FunctionId => "test-function";
        public override TraceContext TraceContext => null!;
        public override BindingContext BindingContext => null!;
        public override RetryContext RetryContext => null!;
        public override IServiceProvider InstanceServices
        {
            get; set;
        } = new ServiceCollection()
            .AddSingleton(markers)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer())
            .BuildServiceProvider();
        public override FunctionDefinition FunctionDefinition => null!;
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => null!;
        public override CancellationToken CancellationToken => CancellationToken.None;
    }

    private sealed class UnmarkedAccounts : IAccountDeletionMarkerRepository
    {
        public Task<bool> MarkAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TryBeginCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryBeginOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MarkedAccounts : IAccountDeletionMarkerRepository
    {
        public Task<bool> MarkAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> TryBeginCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryBeginOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TrackingOperations : IAccountDeletionMarkerRepository
    {
        private bool _active;
        private bool _marked;

        public Task<bool> MarkAsync(string userId, CancellationToken cancellationToken = default)
        {
            _marked = true;

            return Task.FromResult(_active);
        }

        public Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(_marked);
        public Task<bool> TryBeginCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task EndCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryBeginOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default)
        {
            if (_marked)
            {
                return Task.FromResult(false);
            }
            _active = true;

            return Task.FromResult(true);
        }
        
        public Task EndOperationAsync(string userId, string operationId, CancellationToken cancellationToken = default)
        {
            _active = false;

            return Task.CompletedTask;
        }
    }

}
