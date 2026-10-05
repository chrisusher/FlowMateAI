using System.Text.Json;

namespace Tests.API.Tests;

[TestFixture]
public sealed class KeycloakRealmImportTests
{
    [Test]
    public void RealmImportDefinesProfileAndApiAudienceScopesForTheWebClient()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "flowmate-realm.json")));
        var realm = document.RootElement;
        
        var webClient = realm.GetProperty("clients").EnumerateArray()
            .Single(client => client.GetProperty("clientId").GetString() == "flowmate-web");

        var defaultScopes = webClient.GetProperty("defaultClientScopes").EnumerateArray()
            .Select(scope => scope.GetString())
            .ToArray();

        var clientScopes = realm.GetProperty("clientScopes").EnumerateArray().ToArray();

        Assert.That(defaultScopes, Does.Contain("flowmate-api-audience"));
        Assert.That(defaultScopes, Does.Contain("flowmate-user-claims"));

        var audienceScope = clientScopes.Single(scope => scope.GetProperty("name").GetString() == "flowmate-api-audience");

        var audienceMapper = audienceScope.GetProperty("protocolMappers").EnumerateArray()
            .Single(mapper => mapper.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper");

        Assert.That(audienceMapper.GetProperty("config").GetProperty("included.client.audience").GetString(), Is.EqualTo("flowmate-api"));
        Assert.That(audienceMapper.GetProperty("config").GetProperty("access.token.claim").GetString(), Is.EqualTo("true"));
    }

    [Test]
    public void RealmImportMapsTheUserClaimsConsumedByTheWebClient()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "flowmate-realm.json")));

        var claimsScope = document.RootElement.GetProperty("clientScopes").EnumerateArray()
            .Single(scope => scope.GetProperty("name").GetString() == "flowmate-user-claims");

        var mappers = claimsScope.GetProperty("protocolMappers").EnumerateArray().ToArray();

        Assert.That(mappers.Any(mapper => mapper.GetProperty("protocolMapper").GetString() == "oidc-sub-mapper"), Is.True);
        AssertClaimMapper(mappers, "email_verified", "emailVerified", "boolean");
        AssertClaimMapper(mappers, "email", "email", "String");
        AssertClaimMapper(mappers, "preferred_username", "username", "String");
        AssertClaimMapper(mappers, "given_name", "firstName", "String");
        AssertClaimMapper(mappers, "family_name", "lastName", "String");
        Assert.That(mappers.Any(mapper => mapper.GetProperty("protocolMapper").GetString() == "oidc-full-name-mapper"), Is.True);
    }

    private static void AssertClaimMapper(JsonElement[] mappers, string claim, string userAttribute, string jsonType)
    {
        var mapper = mappers.Single(item =>
        {
            var config = item.GetProperty("config");

            return config.TryGetProperty("claim.name", out var claimName) && claimName.GetString() == claim;
        });
        var mapperConfig = mapper.GetProperty("config");

        Assert.That(mapperConfig.GetProperty("user.attribute").GetString(), Is.EqualTo(userAttribute));
        Assert.That(mapperConfig.GetProperty("jsonType.label").GetString(), Is.EqualTo(jsonType));
        Assert.That(mapperConfig.GetProperty("access.token.claim").GetString(), Is.EqualTo("true"));
        Assert.That(mapperConfig.GetProperty("id.token.claim").GetString(), Is.EqualTo("true"));
    }
}
