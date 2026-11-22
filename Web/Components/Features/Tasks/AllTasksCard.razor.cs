using Microsoft.AspNetCore.Components;

namespace Web.Components.Features.Tasks;

public partial class AllTasksCard
{
    [Parameter] public string? ProjectId { get; set; }

    private IEnumerable<TaskRecord> VisibleTasks => ProjectId is null
        ? Store.Data.Tasks
        : Store.Data.Tasks.Where(task => task.ProjectId == ProjectId);

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
        Observe(Tasks);
    }


}
