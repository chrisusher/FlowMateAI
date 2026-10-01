using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace API.Security;

public sealed class Auth0TokenValidator(IConfiguration configuration, IHttpClientFactory clients)
{
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private Dictionary<string, JsonElement> _keys = new(StringComparer.Ordinal);
    private DateTimeOffset _keysExpireAt;

    public async Task<ClaimsPrincipal?> ValidateAsync(string? authorization, CancellationToken cancellationToken = default)
    {
        var token = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorization[7..].Trim()
            : "";
        var pieces = token.Split('.');

        if (pieces.Length != 3)
        {
            return null;
        }

        try
        {
            using var header = JsonDocument.Parse(Decode(pieces[0]));
            using var payload = JsonDocument.Parse(Decode(pieces[1]));
            var headerRoot = header.RootElement;
            var body = payload.RootElement;

            if (!headerRoot.TryGetProperty("alg", out var alg) || alg.GetString() != "RS256" ||
                !headerRoot.TryGetProperty("kid", out var kidValue))
            {
                return null;
            }

            var issuer = configuration["Auth0:Authority"]?.TrimEnd('/') + "/";
            var audience = configuration["Auth0:Audience"];

            if (string.IsNullOrWhiteSpace(configuration["Auth0:Authority"]) || string.IsNullOrWhiteSpace(audience))
            {
                return null;
            }

            if (!body.TryGetProperty("iss", out var iss) || iss.GetString() != issuer || !HasAudience(body, audience))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!body.TryGetProperty("exp", out var exp) || exp.GetInt64() <= now)
            {
                return null;
            }

            if (body.TryGetProperty("nbf", out var nbf) && nbf.GetInt64() > now + 30)
            {
                return null;
            }

            var kid = kidValue.GetString();

            if (string.IsNullOrWhiteSpace(kid))
            {
                return null;
            }
            var key = await GetKeyAsync(kid, issuer, cancellationToken);

            if (key is null)
            {
                return null;
            }

            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Decode(key.Value.GetProperty("n").GetString()!),
                Exponent = Decode(key.Value.GetProperty("e").GetString()!)
            });

            if (!rsa.VerifyData(Encoding.ASCII.GetBytes(pieces[0] + "." + pieces[1]), Decode(pieces[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return null;
            }

            var claims = new List<Claim>();

            foreach (var property in body.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    claims.Add(new(property.Name, property.Value.GetString()!));
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    claims.AddRange(property.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => new Claim(property.Name, v.GetString()!)));
                }
            }

            if (!claims.Any(c => c.Type == "sub"))
            {
                return null;
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Auth0", "name", "role"));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or HttpRequestException or InvalidOperationException or KeyNotFoundException)
        {

            return null;
        }
    }

    private static bool HasAudience(JsonElement body, string audience)
    {
        if (!body.TryGetProperty("aud", out var aud))
        {
            return false;
        }

        return aud.ValueKind == JsonValueKind.String ? aud.GetString() == audience :
            aud.ValueKind == JsonValueKind.Array && aud.EnumerateArray().Any(item => item.GetString() == audience);
    }

    private async Task<JsonElement?> GetKeyAsync(string kid, string issuer, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow >= _keysExpireAt || !_keys.ContainsKey(kid))
        {
            await _keyLock.WaitAsync(cancellationToken);

            try
            {
                if (DateTimeOffset.UtcNow >= _keysExpireAt || !_keys.ContainsKey(kid))
                {
                    using var response = await clients.CreateClient().GetAsync(issuer + ".well-known/jwks.json", cancellationToken);

                    response.EnsureSuccessStatusCode();

                    using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));

                    _keys = document.RootElement.GetProperty("keys").EnumerateArray()
                        .Where(k => k.TryGetProperty("kid", out _))
                        .ToDictionary(k => k.GetProperty("kid").GetString()!, k => k.Clone(), StringComparer.Ordinal);

                    _keysExpireAt = DateTimeOffset.UtcNow.AddHours(4);
                }
            }
            finally
            {
                _keyLock.Release();
            }
        }

        return _keys.TryGetValue(kid, out var value) ? value : null;
    }

    private static byte[] Decode(string value)
    {
        var normalised = value.Replace('-', '+').Replace('_', '/');
        normalised = normalised.PadRight(normalised.Length + (4 - normalised.Length % 4) % 4, '=');

        return Convert.FromBase64String(normalised);
    }
}
