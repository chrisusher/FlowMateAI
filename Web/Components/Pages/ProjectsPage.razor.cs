using Microsoft.AspNetCore.Components.Web;

namespace Web.Components.Pages;

public partial class ProjectsPage
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Projects);
    }

    private void HandleArchiveKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            Projects.CloseArchiveDialog();
        }
    }
}
