using Microsoft.JSInterop;

namespace Web.Clients;

public sealed class Auth0Client(IJSRuntime js, IConfiguration configuration) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private bool _initialised;

    public AuthSession Session { get; private set; } = new();

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
        _initialised = true;
    }

    public async Task LoginAsync(string? connection = null) => await (await ModuleAsync()).InvokeVoidAsync(
        "login",
        new
        {
            domain = Domain,
            clientId = ClientId,
            audience = Audience
        },
        connection);

    public async Task LogoutAsync() => await (await ModuleAsync()).InvokeVoidAsync("logout", new
    {
        domain = Domain,
        clientId = ClientId,
        audience = Audience
    });

    private async Task<IJSObjectReference> ModuleAsync() => _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/auth0.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
