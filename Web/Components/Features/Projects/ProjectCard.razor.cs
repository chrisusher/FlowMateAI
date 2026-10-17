using Microsoft.AspNetCore.Components;
using Web.Managers;

namespace Web.Components.Features.Projects;

public partial class ProjectCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }

    [Parameter, EditorRequired] public ProjectRecord Project { get; set; } = default!;
    [Inject] private WorkspaceProjectManager Projects { get; set; } = default!;
    private List<TaskRecord> ProjectTasks => Store.Data.Tasks.Where(t => t.ProjectId == Project.Id).ToList();
}
