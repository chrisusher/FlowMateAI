using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MCP.Functions.Health;

public sealed class McpHealthCheck(ILogger<McpHealthCheck> logger)
{
    [Function("McpHealthCheck")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request)
    {
        logger.LogInformation("MCP health check processed a request.");
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync("Healthy");

        return response;
    }
}
