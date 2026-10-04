using System.Net.Http.Headers;
using Microsoft.JSInterop;
using Shared.Enums;

namespace Web.Clients;

public sealed class AuthClient(IJSRuntime js, IConfiguration configuration) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<AuthClient>? _reference;
    private Task? _initialization;
    public AuthSession Session { get; private set; } = new();
    public event Action? Changed;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Keycloak:Url"]) &&
        !string.IsNullOrWhiteSpace(configuration["Keycloak:Realm"]) &&
        !string.IsNullOrWhiteSpace(configuration["Keycloak:ClientId"]);

    public Task InitialiseAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        var module = await ModuleAsync();
        Session = await module.InvokeAsync<AuthSession>("initialize", new
        {
            url = configuration["Keycloak:Url"],
            realm = configuration["Keycloak:Realm"],
            clientId = configuration["Keycloak:ClientId"]
        });
        _reference = DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("subscribe", _reference);
    }

    public async Task LoginAsync(SignInProvider provider = SignInProvider.Email)
    {
        await InitialiseAsync();
        var alias = provider switch
        {
            SignInProvider.Email => null,
            SignInProvider.Google => configuration["Keycloak:IdentityProviders:Google"] ?? "google",
            SignInProvider.GitHub => configuration["Keycloak:IdentityProviders:GitHub"] ?? "github",
            SignInProvider.MicrosoftPersonal => configuration["Keycloak:IdentityProviders:MicrosoftPersonal"] ?? "microsoft-personal",
            SignInProvider.MicrosoftWorkSchool => configuration["Keycloak:IdentityProviders:MicrosoftWorkSchool"] ?? "microsoft-work-school",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        await (await ModuleAsync()).InvokeVoidAsync("login", alias);
    }

    public async Task RegisterAsync()
    {
        await InitialiseAsync();
        await (await ModuleAsync()).InvokeVoidAsync("register");
    }

    public async Task LogoutAsync() => await (await ModuleAsync()).InvokeVoidAsync("logout");

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await InitialiseAsync();
        var token = await (await ModuleAsync()).InvokeAsync<string?>("getAccessToken", cancellationToken);

        if (string.IsNullOrWhiteSpace(token) && Session.SignedIn)
        {
            Session = new() { Configured = IsConfigured };
            Changed?.Invoke();
        }

        return token;
    }

    public async Task<HttpRequestMessage> AuthorizedRequestAsync(HttpMethod method, string path, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(method, path);

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [JSInvokable]
    public void SessionChanged(AuthSession session)
    {
        Session = session;
        Changed?.Invoke();
    }

    private async Task<IJSObjectReference> ModuleAsync() => _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/auth.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("subscribe", (object?)null);
            await _module.DisposeAsync();
        }

        _reference?.Dispose();
    }
}
