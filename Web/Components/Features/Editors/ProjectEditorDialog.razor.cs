using Microsoft.AspNetCore.Components.Web;

namespace Web.Components.Features.Editors;

public partial class ProjectEditorDialog
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Projects);
    }

    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            Projects.CloseProjectDialog();
        }
    }
}
