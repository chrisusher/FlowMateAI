namespace Web.Components.Features.Editors;

public partial class TaskEditorDialog
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Tasks);
    }


}
