using System.Net;
using Services.Billing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace API.Functions;

public sealed class StripeWebhook(IBillingService billing)
{
    [Function("StripeWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/stripe")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = request.Headers.TryGetValues("Stripe-Signature", out var values) ? values.FirstOrDefault() : null;
        var accepted = await billing.ProcessWebhookAsync(payload, signature, cancellationToken);
        var response = request.CreateResponse(accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        if (!accepted) await response.WriteStringAsync("Invalid Stripe webhook signature or payload.", cancellationToken);
        return response;
    }
}
