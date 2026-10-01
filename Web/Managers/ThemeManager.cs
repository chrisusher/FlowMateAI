using Microsoft.JSInterop;

namespace Web.Managers;

/// <summary>
/// Service for managing application theme (light/dark mode)
/// </summary>
public sealed class ThemeManager : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<ThemeManager> _logger;
    private bool _isDarkMode = false;
    private IJSObjectReference? _module;

    public event Action<bool>? ThemeChanged;

    public ThemeManager(IJSRuntime jsRuntime, ILogger<ThemeManager> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current theme state
    /// </summary>
    public bool IsDarkMode => _isDarkMode;

    /// <summary>
    /// Initialise theme service and load saved preference
    /// </summary>
    public async Task InitialiseAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            var savedTheme = await module.InvokeAsync<string?>("getSavedTheme");

            if (savedTheme == "dark" || savedTheme == "light")
            {
                _isDarkMode = savedTheme == "dark";
            }
            else
            {
                _isDarkMode = await module.InvokeAsync<bool>("prefersDarkTheme");
            }

            await ApplyThemeAsync();
            _logger.LogInformation("Theme service initialised. Dark mode: {IsDarkMode}", _isDarkMode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initialising theme service");
        }
    }

    /// <summary>
    /// Toggle between light and dark themes
    /// </summary>
    public async Task ToggleThemeAsync()
    {
        _isDarkMode = !_isDarkMode;
        await ApplyThemeAsync();
        await SavePreferenceAsync();

        ThemeChanged?.Invoke(_isDarkMode);
        _logger.LogDebug("Theme toggled to: {Theme}", _isDarkMode ? "dark" : "light");
    }

    /// <summary>
    /// Set specific theme
    /// </summary>
    public async Task SetThemeAsync(bool isDarkMode)
    {
        if (_isDarkMode == isDarkMode)
        {
            return;
        }

        _isDarkMode = isDarkMode;
        await ApplyThemeAsync();
        await SavePreferenceAsync();

        ThemeChanged?.Invoke(_isDarkMode);
        _logger.LogDebug("Theme set to: {Theme}", _isDarkMode ? "dark" : "light");
    }

    private async Task ApplyThemeAsync()
    {
        try
        {
            var theme = _isDarkMode ? "dark" : "light";
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("applyTheme", theme);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying theme");
        }
    }

    private async Task SavePreferenceAsync()
    {
        try
        {
            var theme = _isDarkMode ? "dark" : "light";
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("saveTheme", theme);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving theme preference");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");

        return _module;
    }
}
