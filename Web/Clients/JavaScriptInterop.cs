using Microsoft.JSInterop;

namespace Web.Clients;

internal static class JavaScriptInterop
{
    public static async ValueTask<T> InvokeCheckedAsync<T>(this IJSRuntime runtime, string identifier, params object?[]? args)
    {
        try
        {
            return await runtime.InvokeAsync<T>(identifier, args);
        }
        catch (JSException exception)
        {
            throw new JSException($"JavaScript operation '{identifier}' failed: {exception.Message}", exception);
        }
    }

    public static async ValueTask<T> InvokeCheckedAsync<T>(this IJSObjectReference module, string identifier, params object?[]? args)
    {
        try
        {
            return await module.InvokeAsync<T>(identifier, args);
        }
        catch (JSException exception)
        {
            throw new JSException($"JavaScript operation '{identifier}' failed: {exception.Message}", exception);
        }
    }

    public static async ValueTask InvokeCheckedVoidAsync(this IJSRuntime runtime, string identifier, params object?[]? args)
    {
        try
        {
            await runtime.InvokeVoidAsync(identifier, args);
        }
        catch (JSException exception)
        {
            throw new JSException($"JavaScript operation '{identifier}' failed: {exception.Message}", exception);
        }
    }

    public static async ValueTask InvokeCheckedVoidAsync(this IJSObjectReference module, string identifier, params object?[]? args)
    {
        try
        {
            await module.InvokeVoidAsync(identifier, args);
        }
        catch (JSException exception)
        {
            throw new JSException($"JavaScript operation '{identifier}' failed: {exception.Message}", exception);
        }
    }
}
