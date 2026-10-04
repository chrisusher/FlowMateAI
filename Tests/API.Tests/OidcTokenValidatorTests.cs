using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;

namespace Tests.API.Tests;

[TestFixture]
public sealed class OidcTokenValidatorTests
{
    private const string Authority = "https://keycloak.test/realms/flowmate";
    private const string Audience = "flowmate-api";

    [Test]
    public void EveryApplicationFunctionExceptHealthAndStripeWebhookInjectsTokenValidator()
    {
        var functionTypes = typeof(OidcTokenValidator).Assembly.GetTypes()
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
                .Any(parameter => parameter.ParameterType == typeof(OidcTokenValidator))))
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
        var token = CreateToken(key, subject: "keycloak-user-1");

        var principal = await validator.ValidateAsync($"Bearer {token}");

        Assert.That(principal?.FindFirst("sub")?.Value, Is.EqualTo("keycloak-user-1"));
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
    public async Task ValidateAsync_RetriesDiscoveryWhenRealmSigningKeyRotates()
    {
        using var oldKey = RSA.Create(2048);
        using var rotatedKey = RSA.Create(2048);
        var handler = new TestHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { issuer = Authority, jwks_uri = Authority + "/protocol/openid-connect/certs" }),
            CreateJwks(oldKey, "old-key"),
            timeout: false,
            rotatedJwks: CreateJwks(rotatedKey, "rotated-key"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Authority"] = Authority,
            ["Authentication:Audience"] = Audience
        }).Build();
        var validator = new OidcTokenValidator(configuration, new TestHttpClientFactory(handler));

        var token = $"Bearer {CreateToken(rotatedKey, keyId: "rotated-key")}";
        Assert.That(await validator.ValidateAsync(token), Is.Null);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var principal = await validator.ValidateAsync(token);

        Assert.That(principal?.FindFirst("sub")?.Value, Is.EqualTo("keycloak-user-1"));
        Assert.That(handler.CertificateRequests, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task ValidateAsync_FailsClosedWhenConfigurationIsMissing()
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

    private static OidcTokenValidator CreateValidator(
        RSA key,
        HttpStatusCode status = HttpStatusCode.OK,
        bool includeConfiguration = true,
        bool timeout = false)
    {
        var settings = new Dictionary<string, string?>();

        if (includeConfiguration)
        {
            settings["Authentication:Authority"] = Authority;
            settings["Authentication:Audience"] = Audience;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var jwks = CreateJwks(key, "test-key");
        var discovery = JsonSerializer.Serialize(new
        {
            issuer = Authority,
            jwks_uri = Authority + "/protocol/openid-connect/certs"
        });
        var clients = new TestHttpClientFactory(new TestHttpMessageHandler(status, discovery, jwks, timeout));

        return new OidcTokenValidator(configuration, clients);
    }

    private static string CreateJwks(RSA key, string keyId)
    {
        var publicKey = key.ExportParameters(false);

        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kid = keyId,
                    kty = "RSA",
                    n = Base64Url(publicKey.Modulus!),
                    e = Base64Url(publicKey.Exponent!)
                }
            }
        });
    }

    private static string CreateToken(
        RSA key,
        string subject = "keycloak-user-1",
        string audience = Audience,
        bool includeExpiry = true,
        string issuer = Authority,
        string keyId = "test-key",
        long? expiresAt = null,
        long? notBefore = null,
        RSA? signingKey = null)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg = "RS256", kid = keyId, typ = "JWT" })));
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = issuer,
            ["aud"] = audience,
            ["sub"] = subject,
            ["typ"] = "Bearer"
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

    private sealed class TestHttpMessageHandler(
        HttpStatusCode status,
        string discovery,
        string jwks,
        bool timeout,
        string? rotatedJwks = null) : HttpMessageHandler
    {
        private int _certificateRequests;
        public int CertificateRequests => _certificateRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (timeout)
            {
                return Task.FromException<HttpResponseMessage>(new TaskCanceledException("JWKS request timed out."));
            }

            var isCertificateRequest = request.RequestUri!.AbsolutePath.EndsWith("/certs", StringComparison.Ordinal);
            var certificateRequest = isCertificateRequest ? Interlocked.Increment(ref _certificateRequests) : 0;
            var body = isCertificateRequest
                ? certificateRequest > 1 && rotatedJwks is not null ? rotatedJwks : jwks
                : discovery;
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
