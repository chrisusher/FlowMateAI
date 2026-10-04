using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Web.Clients;

public sealed class Auth0AuthenticationStateProvider(Auth0Client auth) : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            await auth.InitialiseAsync();
        }
        catch (JSException)
        {
            return Anonymous();
        }

        if (!auth.IsConfigured || !auth.Session.SignedIn || string.IsNullOrWhiteSpace(auth.Session.Sub))
        {
            return Anonymous();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, auth.Session.Sub),
            new("sub", auth.Session.Sub)
        };

        if (!string.IsNullOrWhiteSpace(auth.Session.Name))
        {
            claims.Add(new(ClaimTypes.Name, auth.Session.Name));
        }

        if (!string.IsNullOrWhiteSpace(auth.Session.Email))
        {
            claims.Add(new(ClaimTypes.Email, auth.Session.Email));
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "Auth0")));
    }

    private static AuthenticationState Anonymous() => new(new ClaimsPrincipal(new ClaimsIdentity()));
}
