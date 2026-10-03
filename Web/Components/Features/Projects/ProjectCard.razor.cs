using Microsoft.AspNetCore.Components;
using Shared.Models;

namespace Web.Components.Features.Projects;

public partial class ProjectCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }

    [Parameter, EditorRequired] public ProjectRecord Project { get; set; } = default!;
    private List<TaskRecord> ProjectTasks => Store.Data.Tasks.Where(t => t.ProjectId == Project.Id).ToList();
}
