using API.Security;
using Services.Reporting;
using Services.Users;

namespace API.Functions.V1.Reports;

public sealed class GetFocusTimeline(Auth0TokenValidator tokens, IUserService users, IFocusTimelineService timeline)
{
    [Function("GetFocusTimeline")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/reports/focus-timeline")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, date, error) = await ReportRequest.ReadAsync(request, tokens, users, cancellationToken);

        if (error is not null)
        {
            return error;
        }

        try
        {
            var result = await timeline.GetAsync(userId!, date, cancellationToken);
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(result, cancellationToken);

            return response;
        }
        catch (ReportHistoryUnavailableException)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "history_unavailable", "This week is outside your available report history.", cancellationToken);
        }
        catch (InvalidWorkspaceTimeZoneException)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_time_zone", "The workspace time zone is invalid.", cancellationToken);
        }
    }
}
