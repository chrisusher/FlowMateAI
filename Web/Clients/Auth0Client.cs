using Microsoft.JSInterop;

namespace Web.Clients;

public sealed class Auth0Client(IJSRuntime js, IConfiguration configuration) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _initialised;

    public AuthSession Session { get; private set; } = new();
    public AuthSessionStatus Status { get; private set; } = AuthSessionStatus.Uninitialized;
    public bool IsSessionExpired => Status == AuthSessionStatus.Expired;
    public bool IsDeletionPending { get; private set; }
    public event Action? SessionChanged;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Auth0:Domain"]) &&
                                !string.IsNullOrWhiteSpace(configuration["Auth0:ClientId"]) &&
                                !string.IsNullOrWhiteSpace(configuration["Auth0:Audience"]);

    public string? Domain => configuration["Auth0:Domain"];

    public string? ClientId => configuration["Auth0:ClientId"];

    public string? Audience => configuration["Auth0:Audience"];

    public string GoogleConnection => configuration["Auth0:Connections:Google"] ?? "google-oauth2";

    public string GitHubConnection => configuration["Auth0:Connections:GitHub"] ?? "github";

    public string MicrosoftPersonalConnection => configuration["Auth0:Connections:MicrosoftPersonal"] ?? "windowslive";

    public string MicrosoftWorkSchoolConnection => configuration["Auth0:Connections:MicrosoftWorkSchool"] ?? "azuread";

    public async Task InitialiseAsync()
    {
        if (_initialised)
        {
            return;
        }

        var module = await ModuleAsync();
        Session = await module.InvokeAsync<AuthSession>("initialize", new
        {
            domain = Domain,
            clientId = ClientId,
            audience = Audience
        });
        Status = Session.SessionExpired
            ? AuthSessionStatus.Expired
            : !IsConfigured
                ? AuthSessionStatus.Unconfigured
                : Session.SignedIn
                    ? AuthSessionStatus.SignedIn
                    : AuthSessionStatus.SignedOut;
        _initialised = true;
    }

    public void CheckSessionExpiration()
    {
        if (Status == AuthSessionStatus.SignedIn && Session.ExpiresAt is { } expiresAt &&
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= expiresAt)
        {
            MarkSessionExpired();
        }
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        bool requiresAuthentication = false,
        CancellationToken cancellationToken = default)
    {
        CheckSessionExpiration();

        if ((requiresAuthentication && IsSessionExpired) || (IsSessionExpired && request.Headers.Authorization is not null))
        {
            return ExpiredResponse();
        }

        if (requiresAuthentication && Status == AuthSessionStatus.SignedIn && string.IsNullOrWhiteSpace(Session.AccessToken))
        {
            MarkSessionExpired();

            return ExpiredResponse();
        }

        var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            (request.Headers.Authorization is not null || Status == AuthSessionStatus.SignedIn))
        {
            if (await IsDeletionPendingAsync(http, request, cancellationToken))
            {
                IsDeletionPending = true;
                SessionChanged?.Invoke();
            }
            else
            {
                MarkSessionExpired();
            }
        }

        return response;
    }

    private async Task<bool> IsDeletionPendingAsync(HttpClient http, HttpRequestMessage original, CancellationToken cancellationToken)
    {
        if (original.Headers.Authorization is null)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/account/deletion-status");
            request.Headers.Authorization = original.Headers.Authorization;
            using var response = await http.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode && await response.Content.ReadFromJsonAsync<bool>(cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException ||
                                           exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void MarkSessionExpired()
    {
        if (Status == AuthSessionStatus.Expired)
        {
            return;
        }

        Session.SignedIn = false;
        Session.AccessToken = null;
        Session.SessionExpired = true;
        IsDeletionPending = false;
        Status = AuthSessionStatus.Expired;
        SessionChanged?.Invoke();
    }

    private static HttpResponseMessage ExpiredResponse() => new(HttpStatusCode.Unauthorized)
    {
        Content = JsonContent.Create(new ApiError("session_expired", "Your session expired. Sign in again to continue."))
    };

    public async Task LoginAsync(string? connection = null) => await (await ModuleAsync()).InvokeVoidAsync(
        "login",
        new
        {
            domain = Domain,
            clientId = ClientId,
            audience = Audience
        },
        connection);

    public async Task LogoutAsync()
    {
        await (await ModuleAsync()).InvokeVoidAsync("logout", new
        {
            domain = Domain,
            clientId = ClientId,
            audience = Audience
        });
        Session.SignedIn = false;
        Session.AccessToken = null;
        Session.SessionExpired = false;
        IsDeletionPending = false;
        Status = AuthSessionStatus.SignedOut;
        SessionChanged?.Invoke();
    }

    private async Task<IJSObjectReference> ModuleAsync() => _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/auth0.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
