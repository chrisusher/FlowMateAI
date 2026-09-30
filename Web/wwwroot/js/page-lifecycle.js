let visibilityHandler;

export function subscribeVisibility(dotNetRef) {
    visibilityHandler = () => dotNetRef.invokeMethodAsync('HandlePageVisibilityChanged', !document.hidden);
    document.addEventListener('visibilitychange', visibilityHandler);
    return !document.hidden;
}

export function unsubscribeVisibility() {
    if (visibilityHandler) {
        document.removeEventListener('visibilitychange', visibilityHandler);
        visibilityHandler = undefined;
    }
}
