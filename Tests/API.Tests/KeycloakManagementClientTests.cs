using System.Net;
using System.Text;
using System.Text.Json;
using CLI.Commands.User;
using Microsoft.Extensions.Configuration;
using Shared.Exceptions;

namespace Tests.API.Tests;

[TestFixture]
public sealed class KeycloakManagementClientTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:Url"] = "https://keycloak.test",
            ["Keycloak:Realm"] = "flowmate",
            ["Keycloak:ManagementClientId"] = "flowmate-cli",
            ["Keycloak:ManagementClientSecret"] = "test-secret"
        })
        .Build();

    [Test]
    public async Task FindUserIdsByEmailAsync_PaginatesAndReturnsOnlyExactEmailMatches()
    {
        var handler = new PaginationHandler();
        var client = new KeycloakManagementClient(new HttpClient(handler));

        var ids = await client.FindUserIdsByEmailAsync(Configuration(), "person+test@example.com", CancellationToken.None);

        Assert.That(ids, Is.EqualTo(new[] { "exact-account" }));
        Assert.That(handler.FirstOffsets, Is.EqualTo(new[] { 0, 100 }));
        Assert.That(handler.AuthorizationHeaders, Is.All.EqualTo("Bearer service-token"));
    }

    [Test]
    public void ResolveFromMatches_RequiresExplicitSelectionForAmbiguousEmail()
    {
        var error = Assert.Throws<KeycloakManagementException>(() =>
            KeycloakAccountResolver.ResolveFromMatches("person@example.com", null, ["account-a", "account-b"]));

        Assert.That(error!.Message, Does.Contain("Supply --user-id"));
        Assert.That(error.Message, Does.Contain("No data was changed"));
    }

    [Test]
    public void ResolveFromMatches_RejectsAnIdOutsideTheExactEmailMatches()
    {
        var error = Assert.Throws<KeycloakManagementException>(() =>
            KeycloakAccountResolver.ResolveFromMatches("person@example.com", "other-account", ["matched-account"]));

        Assert.That(error!.Message, Does.Contain("is not among the accounts matched"));
    }

    [Test]
    public void ResolveFromMatches_ReturnsTheRequestedMatchingId()
    {
        var id = KeycloakAccountResolver.ResolveFromMatches("person@example.com", "account-b", ["account-a", "account-b"]);

        Assert.That(id, Is.EqualTo("account-b"));
    }

    [Test]
    public void GetManagementTokenAsync_ReportsMissingRealmConfiguration()
    {
        var client = new KeycloakManagementClient(new HttpClient(new PaginationHandler()));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        Assert.ThrowsAsync<KeycloakManagementException>(async () =>
            await client.GetManagementTokenAsync(configuration, CancellationToken.None));
    }

    private sealed class PaginationHandler : HttpMessageHandler
    {
        public List<int> FirstOffsets { get; } = [];
        public List<string?> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "{\"access_token\":\"service-token\"}");
            }

            if (!request.RequestUri.AbsolutePath.EndsWith("/admin/realms/flowmate/users", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.NotFound, "{}");
            }

            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            var first = int.Parse(QueryValue(request.RequestUri.Query, "first"));
            FirstOffsets.Add(first);

            if (first == 0)
            {
                var rows = Enumerable.Range(0, 100)
                    .Select(index => new { id = $"unrelated-{index}", email = "person+test@example.com.attacker" })
                    .ToArray();

                return JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(rows));
            }

            return JsonResponse(HttpStatusCode.OK,
                "[{\"id\":\"exact-account\",\"email\":\"PERSON+TEST@example.com\"}]");
        }

        private static string QueryValue(string query, string key) => query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => Uri.UnescapeDataString(parts[0]) == key)
            .Select(parts => Uri.UnescapeDataString(parts.Length > 1 ? parts[1].Replace('+', ' ') : ""))
            .First();

        private static Task<HttpResponseMessage> JsonResponse(HttpStatusCode status, string json) => Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
