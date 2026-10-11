using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Services.Billing;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Billing;

[TestFixture]
public sealed class StripeBillingServiceTests
{
    [Test]
    public async Task RepeatedCheckoutReusesOpenSessionAndStableIdempotencyKey()
    {
        var handler = new StripeHandler();
        var repository = new StubBillingRepository();
        var service = CreateService(handler, repository);

        var first = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);
        var retry = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);

        Assert.That(first.Succeeded, Is.True);
        Assert.That(retry.Url, Is.EqualTo(first.Url));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(1));
        Assert.That(handler.CheckoutIdempotencyKeys, Has.Count.EqualTo(1));
        Assert.That(handler.CheckoutIdempotencyKeys[0], Does.Not.Contain("auth0|customer"));
    }

    [Test]
    public async Task CheckoutDoesNotReuseOpenSessionForDifferentPrice()
    {
        var handler = new StripeHandler();
        var service = CreateService(handler, new StubBillingRepository());

        var monthly = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);
        var annual = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", true);
        var annualRetry = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", true);

        Assert.That(monthly.Url, Is.EqualTo("https://checkout.example.test/session/price_monthly"));
        Assert.That(annual.Url, Is.EqualTo("https://checkout.example.test/session/price_annual"));
        Assert.That(annualRetry.Url, Is.EqualTo(annual.Url));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(2));
    }

    [Test]
    public async Task CheckoutDoesNotReuseOpenSessionOutsideSubscriptionMode()
    {
        var handler = new StripeHandler();
        handler.OpenCheckoutSessions.Add(new StripeHandler.OpenCheckoutSession("payment", "price_monthly", "https://checkout.example.test/wrong-mode"));
        var service = CreateService(handler, new StubBillingRepository());

        var result = await service.CreateCheckoutAsync("auth0|customer", "person@example.test", false);

        Assert.That(result.Url, Is.EqualTo("https://checkout.example.test/session/price_monthly"));
        Assert.That(handler.CheckoutSessionCreations, Is.EqualTo(1));
    }

    [Test]
    public async Task EntitledCustomerIsSentToBillingPortalInsteadOfAnotherCheckout()
    {
        var handler = new StripeHandler();
        var repository = new StubBillingRepository
        {
            Entitlement = new BillingEntitlementDocument
            {
                UserId = "user",
                StripeCustomerId = "cus_existing",
                SubscriptionStatus = "trialing"
            }
        };
        var service = CreateService(handler, repository);

        var result = await service.CreateCheckoutAsync("user", null, false);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Url, Is.EqualTo("https://billing.example.test/portal"));
        Assert.That(handler.Requests.Any(path => path.StartsWith("checkout/sessions", StringComparison.Ordinal)), Is.False);
        Assert.That(handler.Requests.Any(path => path == "billing_portal/sessions"), Is.True);
    }

    [Test]
    public async Task StripeConfirmedSubscriptionIsSentToBillingPortalWhenWebhookIsDelayed()
    {
        var handler = new StripeHandler { SubscriptionStatus = "active" };
        var repository = new StubBillingRepository
        {
            Entitlement = new BillingEntitlementDocument
            {
                UserId = "user",
                StripeCustomerId = "cus_existing",
                SubscriptionStatus = "none"
            }
        };
        var service = CreateService(handler, repository);

        var result = await service.CreateCheckoutAsync("user", null, false);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(handler.Requests.Any(path => path == "billing_portal/sessions"), Is.True);
        Assert.That(handler.CheckoutSessionCreations, Is.Zero);
    }

    private static StripeBillingService CreateService(StripeHandler handler, IBillingRepository repository)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_placeholder",
            ["Stripe:MonthlyPriceId"] = "price_monthly",
            ["Stripe:AnnualPriceId"] = "price_annual",
            ["WebApp:Origin"] = "https://app.example.test"
        }).Build();

        return new(configuration, new TestHttpClientFactory(handler), repository);
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StripeHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<string> CheckoutIdempotencyKeys { get; } = [];
        public int CheckoutSessionCreations { get; private set; }
        public string? SubscriptionStatus { get; init; }
        public List<OpenCheckoutSession> OpenCheckoutSessions { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery.TrimStart('/').Replace("v1/", "", StringComparison.Ordinal);
            Requests.Add(path);

            if (request.RequestUri.AbsolutePath.EndsWith("/customers", StringComparison.Ordinal))
            {
                return JsonResponse("{\"id\":\"cus_created\"}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/subscriptions", StringComparison.Ordinal))
            {
                return JsonResponse(SubscriptionStatus is null
                    ? "{\"data\":[]}"
                    : $"{{\"data\":[{{\"status\":\"{SubscriptionStatus}\"}}]}}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/checkout/sessions", StringComparison.Ordinal)
                && request.Method == HttpMethod.Get)
            {
                var sessions = OpenCheckoutSessions.Select(session => new
                {
                    mode = session.Mode,
                    url = session.Url,
                    line_items = new { data = new[] { new { price = new { id = session.PriceId } } } }
                });

                return JsonResponse(JsonSerializer.Serialize(new { data = sessions }));
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/checkout/sessions", StringComparison.Ordinal))
            {
                CheckoutSessionCreations++;
                CheckoutIdempotencyKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                var priceId = FormValue(form, "line_items[0][price]")!;
                var url = $"https://checkout.example.test/session/{priceId}";
                OpenCheckoutSessions.Add(new OpenCheckoutSession("subscription", priceId, url));

                return JsonResponse(JsonSerializer.Serialize(new { id = "cs_test", url }));
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/billing_portal/sessions", StringComparison.Ordinal))
            {
                return JsonResponse("{\"url\":\"https://billing.example.test/portal\"}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string? FormValue(string body, string key) => body.Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == key)
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        public sealed record OpenCheckoutSession(string Mode, string PriceId, string Url);
    }

    private sealed class StubBillingRepository : IBillingRepository
    {
        public BillingEntitlementDocument Entitlement { get; init; } = new() { UserId = "user" };

        public Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(Entitlement);

        public Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default)
        {
            Entitlement.StripeCustomerId = entitlement.StripeCustomerId;

            return Task.CompletedTask;
        }

        public Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
