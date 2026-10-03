namespace Web.Components.Features.Today;

public partial class FocusTimerCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Timer);
        Observe(Tasks);
        Observe(Appearance);
    }


}
