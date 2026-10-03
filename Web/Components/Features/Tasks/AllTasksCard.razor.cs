namespace Web.Components.Features.Tasks;

public partial class AllTasksCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
        Observe(Tasks);
    }


}
