using Microsoft.AspNetCore.Components;

namespace Web.Components.Features.Tasks;

public partial class AllTasksCard
{
    private bool _startingFocus;
    private bool _showAll;
    private string? _previousProjectId;
    private string? _focusMessage;

    [Parameter] public string? ProjectId { get; set; }

    private IEnumerable<TaskRecord> ProjectTasks => ProjectId is null
        ? Store.Data.Tasks
        : Store.Data.Tasks.Where(task => task.ProjectId == ProjectId);

    private IEnumerable<TaskRecord> VisibleTasks => _showAll ? ProjectTasks : ProjectTasks.Where(task => !task.IsComplete);

    protected override void OnParametersSet()
    {
        if (_previousProjectId != ProjectId)
        {
            _showAll = false;
            _previousProjectId = ProjectId;
        }
    }

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
                _focusMessage = "Could not start focus. Check that the task is active and that you can control the timer.";
            }
        }
        finally
        {
            _startingFocus = false;
        }
    }

}
