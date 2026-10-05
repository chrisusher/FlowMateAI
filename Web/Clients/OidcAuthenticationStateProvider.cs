using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Web.Clients;

public sealed class OidcAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AuthClient _auth;

    public OidcAuthenticationStateProvider(AuthClient auth)
    {
        _auth = auth;
        _auth.Changed += OnChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            await _auth.InitialiseAsync();
        }
        catch (JSException)
        {
            return Anonymous();
        }

        if (!_auth.IsConfigured || !_auth.Session.SignedIn || string.IsNullOrWhiteSpace(_auth.Session.Sub))
        {
            return Anonymous();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _auth.Session.Sub),
            new("sub", _auth.Session.Sub),
            new(ClaimTypes.Name, _auth.Session.Name ?? "FlowMate user"),
            new(ClaimTypes.Email, _auth.Session.Email ?? "")
        };

        return new(new ClaimsPrincipal(new ClaimsIdentity(claims, "OIDC")));
    }

    private void OnChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    private static AuthenticationState Anonymous() => new(new ClaimsPrincipal(new ClaimsIdentity()));
    public void Dispose() => _auth.Changed -= OnChanged;
}
