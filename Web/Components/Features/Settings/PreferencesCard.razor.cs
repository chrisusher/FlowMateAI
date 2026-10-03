namespace Web.Components.Features.Settings;

public partial class PreferencesCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Appearance);
    }


}
