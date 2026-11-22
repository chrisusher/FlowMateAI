using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Web.Clients;

namespace Web.Tests;

public sealed class InteropAndAuthenticationTests : WorkspaceComponentTest
{
    [Test]
    public async Task WorkspaceStorageFailurePreservesTheJavaScriptErrorAndNamesTheOperation()
    {
        var original = new JSException("Storage is unavailable.");
        WorkspaceModule.Setup<string?>("read", _ => true).SetException(original);

        var error = await Assert.ThrowsAsync<JSException>(async () => await Store.InitialiseAsync());

        Assert.That(error!.Message, Does.Contain("read").And.Contain("Storage is unavailable."));
        Assert.That(error.InnerException, Is.SameAs(original));
    }

    [Test]
    public async Task AuthenticationStateNotificationReflectsSessionExpiry()
    {
        await SignInAsync();
        using var provider = new Auth0AuthenticationStateProvider(Auth);
        Assert.That((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated, Is.True);
        Task<AuthenticationState>? changedState = null;
        provider.AuthenticationStateChanged += state => changedState = state;

        Auth.Session.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds();
        Auth.CheckSessionExpiration();

        Assert.That(changedState, Is.Not.Null);
        Assert.That((await changedState!).User.Identity!.IsAuthenticated, Is.False);
    }
}
