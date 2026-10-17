namespace Web.Components.Features.Settings;

public partial class ProfileCard
{
    private readonly List<string> _timeZones =
    [
        "Europe/London",
        "America/New_York",
        "America/Los_Angeles",
        "Europe/Paris",
        "Asia/Tokyo"
    ];

    protected override void OnInitialized()
    {
        base.OnInitialized();

        if (!string.IsNullOrWhiteSpace(Store.Data.TimeZone) && !_timeZones.Contains(Store.Data.TimeZone))
        {
            // Keep the saved or detected zone available even after selecting another option.
            _timeZones.Add(Store.Data.TimeZone);
        }
    }
}
