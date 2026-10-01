using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace API.Functions.V1.Health;

public sealed class HealthCheck(ILogger<HealthCheck> logger)
{
    [Function("HealthCheck")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/health")] HttpRequestData request)
    {
        logger.LogInformation("HealthCheck HTTP trigger processed a request.");
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync("Healthy");

        return response;
    }
}
