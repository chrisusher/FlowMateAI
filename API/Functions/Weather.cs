using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace API.Functions;

public sealed class Weather
{
    private static readonly string[] Summaries =
        ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

    [Function("WeatherForecast")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "weatherforecast")] HttpRequestData request)
    {
        // Sample data for the template's Weather page.
        var forecasts = Enumerable.Range(1, 5).Select(day => new
        {
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(day)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        });

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(forecasts);
        return response;
    }
}
