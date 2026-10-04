using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Web.Clients;

public sealed class Auth0AuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly Auth0Client _auth;

    public Auth0AuthenticationStateProvider(Auth0Client auth)
    {
        _auth = auth;
        _auth.SessionChanged += OnSessionChanged;
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
            new("sub", _auth.Session.Sub)
        };

        if (!string.IsNullOrWhiteSpace(_auth.Session.Name))
        {
            claims.Add(new(ClaimTypes.Name, _auth.Session.Name));
        }

        if (!string.IsNullOrWhiteSpace(_auth.Session.Email))
        {
            claims.Add(new(ClaimTypes.Email, _auth.Session.Email));
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "Auth0")));
    }

    private void OnSessionChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose()
    {
        _auth.SessionChanged -= OnSessionChanged;
        GC.SuppressFinalize(this);
    }

    private static AuthenticationState Anonymous() => new(new ClaimsPrincipal(new ClaimsIdentity()));
}
