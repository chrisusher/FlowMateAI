using ChrisUsher.Core.Shared.Enums;
using ChrisUsher.Core.Shared.UI;

namespace Web.Managers;

/// <summary>
/// Provides a simple publish/subscribe toast notification manager for the Blazor WebAssembly UI.
/// </summary>
public sealed class ToastManager
{
    public event Action<ToastMessage>? OnToast;

    public void Success(string message, string? title = null, int timeoutMs = 5000) =>
        Show(message, title ?? "Success", ToastLevel.Success, timeoutMs);

    public void Info(string message, string? title = null, int timeoutMs = 5000) =>
        Show(message, title ?? "Info", ToastLevel.Info, timeoutMs);

    public void Warning(string message, string? title = null, int timeoutMs = 7000) =>
        Show(message, title ?? "Warning", ToastLevel.Warning, timeoutMs);

    public void Error(string message, string? title = null, int timeoutMs = 8000) =>
        Show(message, title ?? "Error", ToastLevel.Error, timeoutMs);

    private void Show(string message, string title, ToastLevel level, int timeoutMs)
    {
        var toast = new ToastMessage
        {
            Id = Guid.NewGuid(),
            Title = title,
            Message = message,
            Level = level,
            TimeoutMs = timeoutMs,
            Timestamp = DateTime.UtcNow
        };
        OnToast?.Invoke(toast);
    }
}
