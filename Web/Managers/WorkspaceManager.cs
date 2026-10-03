namespace Web.Managers;

public abstract class WorkspaceManager
{
    public event Action? Changed;
    protected void NotifyChanged() => Changed?.Invoke();
}
