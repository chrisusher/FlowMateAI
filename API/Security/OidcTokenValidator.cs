using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace API.Security;

public sealed class OidcTokenValidator
{
    private readonly string? _authority;
    private readonly string? _audience;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _metadata;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public OidcTokenValidator(IConfiguration configuration, IHttpClientFactory clients)
    {
        _authority = configuration["Authentication:Authority"];
        _audience = configuration["Authentication:Audience"];

        if (Uri.TryCreate(_authority, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo))
        {
            var documents = new HttpDocumentRetriever(clients.CreateClient()) { RequireHttps = !uri.IsLoopback };
            _metadata = new ConfigurationManager<OpenIdConnectConfiguration>(
                _authority.TrimEnd('/') + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(), documents);
            _metadata.RefreshInterval = TimeSpan.FromSeconds(1);
        }
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string? authorization, CancellationToken cancellationToken = default)
    {
        if (_metadata is null || string.IsNullOrWhiteSpace(_audience) ||
            authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var token = authorization[7..].Trim();

        try
        {
            // IdentityModel caches discovery and keys, and retries validation on key rotation.
            // Compare the configured issuer exactly, independently of discovery's issuer.
            var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ConfigurationManager = _metadata,
                ValidIssuer = _authority,
                IssuerValidator = (issuer, _, _) => issuer == _authority
                    ? issuer : throw new SecurityTokenInvalidIssuerException("Unexpected realm issuer."),
                ValidAudience = _audience,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                TryAllIssuerSigningKeys = false,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "name",
                RoleClaimType = "role",
                AuthenticationType = "OIDC"
            });
            cancellationToken.ThrowIfCancellationRequested();

            if (!result.IsValid || result.ClaimsIdentity is null ||
                string.IsNullOrWhiteSpace(result.ClaimsIdentity.FindFirst("sub")?.Value) ||
                result.ClaimsIdentity.FindFirst("typ")?.Value != "Bearer")
            {
                return null;
            }

            return new ClaimsPrincipal(result.ClaimsIdentity);
        }
        catch (Exception exception) when (exception is SecurityTokenException or HttpRequestException or
            InvalidOperationException or ArgumentException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
