namespace Web.Components.Pages;

public partial class TasksPage
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Tasks);
    }
}
