namespace Web.Components.Features.Settings;

public partial class ProfileCard
{
    private bool _confirmingDeletion;
    private bool _deleting;
    private string _deletionEmail = "";
    private string _deletionConfirmation = "";
    private string? _deletionMessage;

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

    private void BeginDeletion()
    {
        _confirmingDeletion = true;
        _deletionEmail = Auth.Session.Email ?? "";
        _deletionConfirmation = "";
        _deletionMessage = null;
    }

    private void CancelDeletion()
    {
        _confirmingDeletion = false;
        _deletionMessage = null;
    }

    private async Task DeleteAccountAsync()
    {
        if (_deleting)
        {
            return;
        }

        _deleting = true;
        _deletionMessage = null;

        try
        {
            var error = await Account.DeleteAsync(_deletionEmail, _deletionConfirmation);

            if (error is not null)
            {
                _deletionMessage = error.Message;

                return;
            }

            await Store.ClearLocalDataAsync();
            await Auth.LogoutAsync();
            Navigation.NavigateTo("", replace: true);
        }
        finally
        {
            _deleting = false;
        }
    }
}
