namespace Web.Components.Features.Editors;

public partial class ProjectEditorDialog
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Projects);
    }


}
