using Microsoft.AspNetCore.Components;

namespace Web.Components.Features.Tasks;

public partial class AllTasksCard
{
    private bool _startingFocus;
    private string? _focusMessage;

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

    private async Task StartFocusAsync(TaskRecord task)
    {
        if (_startingFocus)
        {
            return;
        }

        _startingFocus = true;
        _focusMessage = null;

        try
        {
            if (await Timer.StartFocusForTaskAsync(task.Id))
            {
                Go("today");
            }
            else
            {
                _focusMessage = "Could not start focus. Check that the task is active and no other session is in progress.";
            }
        }
        finally
        {
            _startingFocus = false;
        }
    }

}
