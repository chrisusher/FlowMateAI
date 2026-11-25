using System.Globalization;
using API.Security;
using Services.Reporting;
using Services.Users;

namespace API.Functions.V1.Reports;

public sealed class GetFocusTimeline(Auth0TokenValidator tokens, IUserService users, IFocusTimelineService timeline)
{
    [Function("GetFocusTimeline")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/reports/focus-timeline")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var rawDate = request.Query.GetValues("date");
        DateOnly? date = null;

        if (rawDate is not null)
        {
            if (rawDate.Length != 1 || !DateOnly.TryParseExact(rawDate[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed.Year is < 2 or > 9998)
            {
                return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_date", "Date must be a valid date in yyyy-MM-dd format.", cancellationToken);
            }
            date = parsed;
        }

        try
        {
            var result = await timeline.GetAsync(userId, date, cancellationToken);
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
