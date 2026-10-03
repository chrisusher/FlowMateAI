namespace Web.Components.Pages;

public partial class CoachPage
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Coach);
    }
}
