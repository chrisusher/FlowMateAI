using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Shared.Contracts;
using Shared.Enums;

namespace Web.Components.Features.Reports;

public partial class TaskFocusCard
{
    [Parameter, EditorRequired] public DateOnly Date { get; set; }
    private DateOnly? _loadedDate;
    private List<TaskFocus> _tasks = [];
    private bool _loading = true;
    private bool _error;
    private bool _disposed;
    private int _requestNumber;
    private string? _fingerprint;

    protected override void OnInitialized()
    {
        _fingerprint = Fingerprint();
        Store.Changed += WorkspaceChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedDate != Date)
        {
            _loadedDate = Date;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var request = ++_requestNumber;
        _loading = true;
        _error = false;

        try
        {
            var tasks = await Items.GetTaskFocusAsync(Date);

            if (request == _requestNumber && !_disposed)
            {
                _tasks = tasks;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException or JsonException)
        {

            if (request == _requestNumber && !_disposed)
            {
                _error = true;
            }
        }
        finally
        {

            if (request == _requestNumber && !_disposed)
            {
                _loading = false;
            }
        }
    }

    private void WorkspaceChanged()
    {
        if (_disposed || Store.SyncState != WorkspaceSyncState.Saved)
        {
            return;
        }
        var fingerprint = Fingerprint();

        if (fingerprint == _fingerprint)
        {
            return;
        }
        _fingerprint = fingerprint;
        _ = InvokeAsync(async () =>
        {
            await LoadAsync();

            if (!_disposed)
            {
                StateHasChanged();
            }
        });
    }

    private string Fingerprint() => JsonSerializer.Serialize(new { Store.Data.TimeZone, Store.Data.Plan, Store.Data.Sessions, Store.Data.Tasks });

    public void Dispose()
    {
        _disposed = true;
        Store.Changed -= WorkspaceChanged;
        GC.SuppressFinalize(this);
    }
}
