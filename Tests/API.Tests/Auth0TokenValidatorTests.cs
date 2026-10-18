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
using API.Functions.V1;
using Services.Users;

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

    private static Auth0TokenValidator CreateValidator(
        RSA key,
        HttpStatusCode status = HttpStatusCode.OK,
        bool includeConfiguration = true,
        bool timeout = false,
        TestHttpMessageHandler? handler = null)
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

        return new Auth0TokenValidator(configuration, clients);
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
        var header = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "RS256", kid = keyId })));
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
        public int UserInfoRequests { get; private set; }
        public string? UserInfoAuthorization { get; private set; }

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
        public int EmailLookupCalls { get; private set; }
        public int RegistrationCalls { get; private set; }
        public string? RegisteredEmail { get; private set; }

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

    private sealed class TestHttpRequestData(string authorization) : HttpRequestData(new TestFunctionContext())
    {
        public override Stream Body => Stream.Null;
        public override HttpHeadersCollection Headers { get; } = new() { { "Authorization", authorization } };
        public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
        public override Uri Url => new("https://api.flowmate.test/test");
        public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();
        public override string Method => "GET";
        public override HttpResponseData CreateResponse() => throw new NotSupportedException();
    }

    private sealed class TestFunctionContext : FunctionContext
    {
        public override string InvocationId => "test-invocation";
        public override string FunctionId => "test-function";
        public override TraceContext TraceContext => null!;
        public override BindingContext BindingContext => null!;
        public override RetryContext RetryContext => null!;
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition => null!;
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => null!;
        public override CancellationToken CancellationToken => CancellationToken.None;
    }
}
