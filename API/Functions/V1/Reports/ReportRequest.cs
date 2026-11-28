using System.Globalization;
using API.Security;
using Services.Users;

namespace API.Functions.V1.Reports;

internal static class ReportRequest
{
    public static async Task<(string? UserId, DateOnly? Date, HttpResponseData? Error)> ReadAsync(
        HttpRequestData request, Auth0TokenValidator tokens, IUserService users, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return (null, null, await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken));
        }

        var rawDate = request.Query.GetValues("date");

        if (rawDate is null)
        {
            return (userId, null, null);
        }

        if (rawDate.Length != 1 || !DateOnly.TryParseExact(rawDate[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < 2 or > 9998)
        {
            return (null, null, await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_date", "Date must be a valid date in yyyy-MM-dd format.", cancellationToken));
        }

        return (userId, date, null);
    }
}
