using System.Net.Http.Headers;
using System.Net;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using Shared.Contracts;
using Shared.Enums;

namespace Web.Clients;

public sealed class AuthClient(IJSRuntime js, IConfiguration configuration) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<AuthClient>? _reference;
    private Task? _initialization;
    private bool _isSessionExpired;
    public AuthSession Session { get; private set; } = new();
    public AuthSessionStatus Status { get; private set; } = AuthSessionStatus.Uninitialized;
    public bool IsSessionExpired => _isSessionExpired;
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
        _isSessionExpired = Session.SessionExpired;
        Status = Session.SessionExpired
            ? AuthSessionStatus.Expired
            : !IsConfigured
            ? AuthSessionStatus.Unconfigured
            : Session.SignedIn ? AuthSessionStatus.SignedIn : AuthSessionStatus.SignedOut;
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
        var wasSignedIn = Session.SignedIn;
        var token = await (await ModuleAsync()).InvokeAsync<string?>("getAccessToken", cancellationToken);

        if (string.IsNullOrWhiteSpace(token) && wasSignedIn)
        {
            MarkSessionExpired();
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

    public async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        bool requiresAuthentication = false,
        CancellationToken cancellationToken = default)
    {
        if (requiresAuthentication && IsSessionExpired)
        {
            return ExpiredResponse();
        }

        var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            (requiresAuthentication || request.Headers.Authorization is not null))
        {
            MarkSessionExpired();
        }

        return response;
    }

    private void MarkSessionExpired()
    {
        if (_isSessionExpired)
        {
            return;
        }

        _isSessionExpired = true;
        Session.SignedIn = false;
        Session.SessionExpired = true;
        Status = AuthSessionStatus.Expired;
        Changed?.Invoke();
    }

    private static HttpResponseMessage ExpiredResponse() => new(HttpStatusCode.Unauthorized)
    {
        Content = JsonContent.Create(new ApiError("session_expired", "Your session expired. Sign in again to continue."))
    };

    [JSInvokable]
    public void SessionChanged(AuthSession session)
    {
        Session = session;
        _isSessionExpired = session.SessionExpired;
        Status = session.SessionExpired
            ? AuthSessionStatus.Expired
            : !IsConfigured
            ? AuthSessionStatus.Unconfigured
            : Session.SignedIn ? AuthSessionStatus.SignedIn : AuthSessionStatus.SignedOut;
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
