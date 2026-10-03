namespace Web.Components.Pages;

public partial class ProjectsPage
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Projects);
    }
}
